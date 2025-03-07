﻿using ILRuntime.CLR.Method;
using ILRuntime.CLR.TypeSystem;
using ILRuntime.Runtime.Debugger.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ILRuntime.Runtime.Debugger
{
#pragma warning disable
    public class DebuggerServer
    {
        public const int Version = 2;
        TcpListener listener;
        //HashSet<Session<T>> clients = new HashSet<Session<T>>();
        bool isUp = false;
        int maxNewConnections = 1;
        int port;
        Thread mainLoop;
        DebugSocket clientSocket;
        System.IO.MemoryStream sendStream = new System.IO.MemoryStream(64 * 1024);
        System.IO.BinaryWriter bw;
        DebugService ds;

        /// <summary>
        /// 服务器监听的端口
        /// </summary>
        public int Port { get { return port; } set { this.port = value; } }

        public DebugSocket Client { get { return clientSocket; } }

        public bool IsAttached { get { return clientSocket != null && !clientSocket.Disconnected; } }

        public DebuggerServer(DebugService ds)
        {
            this.ds = ds;
            bw = new System.IO.BinaryWriter(sendStream);
        }

        public virtual bool Start()
        {
            mainLoop = new Thread(new ThreadStart(this.NetworkLoop));
            mainLoop.Start();

            this.listener = new TcpListener(port);
            try { listener.Start(); }
            catch
            {
                return false;
            }
            isUp = true;
            return true;
        }

        public virtual void Stop()
        {
            isUp = false;
            if (this.listener != null)
                this.listener.Stop();
            mainLoop.Abort();
            mainLoop = null;
            if (clientSocket != null)
                clientSocket.Close();
        }

        void NetworkLoop()
        {
            while (true)
            {
                try
                {
                    // let new clients (max 10) connect
                    if (isUp && clientSocket == null)
                    {
                        for (int i = 0; listener.Pending() && i < maxNewConnections; i++)
                        {
                            CreateNewSession(listener);
                        }
                    }
                    System.Threading.Thread.Sleep(1);
                }
                catch (ThreadAbortException)
                {
                }
                catch (Exception)
                {

                }
            }
        }

        void CreateNewSession(TcpListener listener)
        {
            Socket sock = listener.AcceptSocket();
            clientSocket = new DebugSocket(sock);
            clientSocket.OnReciveMessage = OnReceive;
            clientSocket.OnClose = OnClose;
            ClientConnected();
        }

        void ClientConnected()
        {

        }

        void OnClose()
        {
            ds.Detach();
            clientSocket = null;
        }

        void OnReceive(DebugMessageType type, byte[] buffer)
        {
            if (clientSocket == null || clientSocket.Disconnected)
                return;
            System.IO.MemoryStream ms = new System.IO.MemoryStream(buffer);
            System.IO.BinaryReader br = new System.IO.BinaryReader(ms);

            switch (type)
            {
                case DebugMessageType.CSAttach:
                    {
                        SendAttachResult();
                    }
                    break;
                case DebugMessageType.CSBindBreakpoint:
                    {
                        CSBindBreakpoint msg = new Protocol.CSBindBreakpoint();
                        msg.BreakpointHashCode = br.ReadInt32();
                        msg.IsLambda = br.ReadBoolean();
                        msg.TypeName = br.ReadString();
                        msg.MethodName = br.ReadString();
                        msg.StartLine = br.ReadInt32();
                        msg.EndLine = br.ReadInt32();
                        TryBindBreakpoint(msg);
                    }
                    break;
                case DebugMessageType.CSDeleteBreakpoint:
                    {
                        CSDeleteBreakpoint msg = new Protocol.CSDeleteBreakpoint();
                        msg.BreakpointHashCode = br.ReadInt32();
                        ds.DeleteBreakpoint(msg.BreakpointHashCode);
                    }
                    break;
                case DebugMessageType.CSExecute:
                    {
                        CSExecute msg = new Protocol.CSExecute();
                        msg.ThreadHashCode = br.ReadInt32();
                        ds.ExecuteThread(msg.ThreadHashCode);
                    }
                    break;
                case DebugMessageType.CSStep:
                    {
                        CSStep msg = new CSStep();
                        msg.ThreadHashCode = br.ReadInt32();
                        msg.StepType = (StepTypes)br.ReadByte();
                        ds.StepThread(msg.ThreadHashCode, msg.StepType);
                    }
                    break;
                case DebugMessageType.CSResolveVariable:
                    {
                        CSResolveVariable msg = new CSResolveVariable();
                        msg.ThreadHashCode = br.ReadInt32();
                        msg.Variable = ReadVariableReference(br);
                        VariableInfo info;
                        try
                        {
                            object res;
                            info = ds.ResolveVariable(msg.ThreadHashCode, msg.Variable, out res);
                        }
                        catch (Exception ex)
                        {
                            info = VariableInfo.GetException(ex);
                        }
                        if (info.Type != VariableTypes.Pending)
                            SendSCResolveVariableResult(info);
                    }
                    break;
                case DebugMessageType.CSResolveIndexAccess:
                    {
                        CSResolveIndexer msg = new CSResolveIndexer();
                        msg.ThreadHashCode = br.ReadInt32();
                        msg.Body = ReadVariableReference(br);
                        msg.Index = ReadVariableReference(br);

                        VariableInfo info;
                        try
                        {
                            object res;
                            info = ds.ResolveIndexAccess(msg.ThreadHashCode, msg.Body, msg.Index, out res);
                        }
                        catch (Exception ex)
                        {
                            info = VariableInfo.GetException(ex);
                        }
                        if (info.Type != VariableTypes.Pending)
                            SendSCResolveVariableResult(info);
                    }
                    break;
                case DebugMessageType.CSEnumChildren:
                    {
                        int thId = br.ReadInt32();
                        var parent = ReadVariableReference(br);

                        VariableInfo[] info = null;
                        try
                        {
                            info = ds.EnumChildren(thId, parent);
                        }
                        catch (Exception ex)
                        {
                            info = new VariableInfo[] { VariableInfo.GetException(ex) };
                        }
                        if (info != null)
                            SendSCEnumChildrenResult(info);
                    }
                    break;
            }

        }

        VariableReference ReadVariableReference(System.IO.BinaryReader br)
        {
            VariableReference res = null;
            if (br.ReadBoolean())
            {
                res = new Debugger.VariableReference();
                res.Address = br.ReadInt64();
                res.Type = (VariableTypes)br.ReadByte();
                res.Offset = br.ReadInt32();
                res.Name = br.ReadString();
                res.Parent = ReadVariableReference(br);
                int cnt = br.ReadInt32();
                res.Parameters = new VariableReference[cnt];
                for (int i = 0; i < cnt; i++)
                {
                    res.Parameters[i] = ReadVariableReference(br);
                }
            }
            return res;
        }

        void SendAttachResult()
        {
            sendStream.Position = 0;
            bw.Write((byte)AttachResults.OK);
            bw.Write(Version);
            DoSend(DebugMessageType.SCAttachResult);
            lock (ds.AppDomain.FreeIntepreters)
            {
                foreach (var i in ds.AppDomain.Intepreters)
                {
                    SendSCThreadStarted(i.Key);
                }
            }
        }

        void DoSend(DebugMessageType type)
        {
            if (clientSocket != null && !clientSocket.Disconnected)
                clientSocket.Send(type, sendStream.GetBuffer(), (int)sendStream.Position);
        }

        bool CheckCompilerGeneratedStateMachine(ILMethod ilm, Enviorment.AppDomain domain, int startLine, out ILMethod found)
        {
            var mDef = ilm.Definition;
            Mono.Cecil.CustomAttribute ca = null;
            found = null;
            foreach (var attr in mDef.CustomAttributes)
            {
                switch (attr.AttributeType.FullName)
                {
                    case "System.Runtime.CompilerServices.AsyncStateMachineAttribute":
                    case "System.Runtime.CompilerServices.IteratorStateMachineAttribute":
                        ca = attr;
                        break;

                }
            }
            if (ca != null)
            {
                if (ca.ConstructorArguments.Count > 0)
                {
                    var smType = domain.GetType(ca.ConstructorArguments[0].Value, null, null);
                    if (smType != null)
                    {
                        ilm = smType.GetMethod("MoveNext", 0, true) as ILMethod;
                        if (ilm != null && ilm.StartLine <= (startLine + 1) && ilm.EndLine >= (startLine + 1))
                        {
                            found = ilm;
                            return true;
                        }
                    }
                }
            }
            return false;
        }
        void TryBindBreakpoint(CSBindBreakpoint msg)
        {
            var domain = ds.AppDomain;
            SCBindBreakpointResult res = new Protocol.SCBindBreakpointResult();
            res.BreakpointHashCode = msg.BreakpointHashCode;
            IType type;
            if (msg.IsLambda)
            {
                ILMethod found = null;
                foreach (var i in domain.LoadedTypes.ToArray())
                {
                    var vt = i.Value as ILType;
                    if (vt != null)
                    {
                        if (vt.FullName.Contains(msg.TypeName))
                        {
                            foreach (var j in vt.GetMethods())
                            {
                                if (j.Name.Contains(string.Format("<{0}>", msg.MethodName)))
                                {
                                    ILMethod ilm = (ILMethod)j;
                                    if (ilm.StartLine <= (msg.StartLine + 1) && ilm.EndLine >= (msg.StartLine + 1))
                                    {
                                        found = ilm;
                                        break;
                                    }
                                    else if (CheckCompilerGeneratedStateMachine(ilm, domain, msg.StartLine, out found))
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    if (found != null)
                        break;
                }
                if (found != null)
                {
                    ds.SetBreakPoint(found.GetHashCode(), msg.BreakpointHashCode, msg.StartLine);
                    res.Result = BindBreakpointResults.OK;
                }
                else
                {
                    res.Result = BindBreakpointResults.CodeNotFound;
                }
            }
            else
            {
                if (domain.LoadedTypes.TryGetValue(msg.TypeName, out type))
                {
                    if (type is ILType)
                    {
                        ILType it = (ILType)type;
                        ILMethod found = null;
                        if (msg.MethodName == ".ctor")
                        {
                            foreach (var i in it.GetConstructors())
                            {
                                ILMethod ilm = (ILMethod)i;
                                if (ilm.StartLine <= (msg.StartLine + 1) && ilm.EndLine >= (msg.StartLine + 1))
                                {
                                    found = ilm;
                                    break;
                                }
                            }
                        }
                        else if (msg.MethodName == ".cctor")
                        {
                            ILMethod ilm = it.GetStaticConstroctor() as ILMethod;
                            if (ilm.StartLine <= (msg.StartLine + 1) && ilm.EndLine >= (msg.StartLine + 1))
                            {
                                found = ilm;
                            }
                        }
                        else
                        {
                            foreach (var i in it.GetMethods())
                            {
                                if (i.Name == msg.MethodName)
                                {
                                    ILMethod ilm = (ILMethod)i;
                                    if (ilm.StartLine <= (msg.StartLine + 1) && ilm.EndLine >= (msg.StartLine + 1))
                                    {
                                        found = ilm;
                                        break;
                                    }
                                    else if (CheckCompilerGeneratedStateMachine(ilm, domain, msg.StartLine, out found))
                                    {
                                        break;
                                    }
                                }
                            }
                        }
                        if (found != null)
                        {
                            ds.SetBreakPoint(found.GetHashCode(), msg.BreakpointHashCode, msg.StartLine);
                            res.Result = BindBreakpointResults.OK;
                        }
                        else
                        {
                            res.Result = BindBreakpointResults.CodeNotFound;
                        }
                    }
                    else
                    {
                        res.Result = BindBreakpointResults.TypeNotFound;
                    }
                }
                else
                {
                    res.Result = BindBreakpointResults.TypeNotFound;
                }
            }
            SendSCBindBreakpointResult(res);
        }

        void SendSCBindBreakpointResult(SCBindBreakpointResult msg)
        {
            sendStream.Position = 0;
            bw.Write(msg.BreakpointHashCode);
            bw.Write((byte)msg.Result);
            DoSend(DebugMessageType.SCBindBreakpointResult);
        }

        internal void SendSCBreakpointHit(int intpHash, int bpHash, KeyValuePair<int, StackFrameInfo[]>[] info)
        {
            sendStream.Position = 0;
            bw.Write(bpHash);
            bw.Write(intpHash);
            WriteStackFrames(info);
            DoSend(DebugMessageType.SCBreakpointHit);
        }

        internal void SendSCStepComplete(int intpHash, KeyValuePair<int, StackFrameInfo[]>[] info)
        {
            sendStream.Position = 0;
            bw.Write(intpHash);
            WriteStackFrames(info);
            DoSend(DebugMessageType.SCStepComplete);
        }

        internal void SendSCResolveVariableResult(VariableInfo info)
        {
            lock (this)
            {
                sendStream.Position = 0;
                WriteVariableInfo(info);
                DoSend(DebugMessageType.SCResolveVariableResult);
            }
        }

        internal void SendSCEnumChildrenResult(VariableInfo[] info)
        {
            lock (this)
            {
                sendStream.Position = 0;
                if (info != null)
                {
                    bw.Write(info.Length);
                    for (int i = 0; i < info.Length; i++)
                    {
                        WriteVariableInfo(info[i]);
                    }
                }
                else
                    bw.Write(0);
                DoSend(DebugMessageType.SCEnumChildrenResult);
            }
        }

        void WriteStackFrames(KeyValuePair<int, StackFrameInfo[]>[] info)
        {
            bw.Write(info.Length);
            foreach (var i in info)
            {
                bw.Write(i.Key);
                bw.Write(i.Value.Length);
                foreach (var j in i.Value)
                {
                    bw.Write(j.MethodName);
                    bw.Write(j.DocumentName);
                    bw.Write(j.StartLine);
                    bw.Write(j.StartColumn);
                    bw.Write(j.EndLine);
                    bw.Write(j.EndColumn);
                    bw.Write(j.LocalVariables.Length);
                    foreach (var k in j.LocalVariables)
                    {
                        WriteVariableInfo(k);
                    }
                }
            }
        }

        void WriteVariableInfo(VariableInfo k)
        {
            bw.Write(k.Address);
            bw.Write((byte)k.Type);
            bw.Write(k.Offset);
            bw.Write(k.Name);
            bw.Write(k.Value);
            bw.Write((byte)k.ValueType);
            bw.Write(k.TypeName);
            bw.Write(k.Expandable);
            bw.Write(k.IsPrivate);
            bw.Write(k.IsProtected);
        }

        internal void SendSCThreadStarted(int threadHash)
        {
            sendStream.Position = 0;
            bw.Write(threadHash);
            DoSend(DebugMessageType.SCThreadStarted);
        }

        internal void SendSCThreadEnded(int threadHash)
        {
            sendStream.Position = 0;
            bw.Write(threadHash);
            DoSend(DebugMessageType.SCThreadEnded);
        }

        public void NotifyModuleLoaded(string modulename)
        {
            sendStream.Position = 0;
            bw.Write(modulename);
            DoSend(DebugMessageType.SCModuleLoaded);
        }
    }
}
