﻿using ILRuntime.CLR.TypeSystem;
using ILRuntime.Mono.Cecil;
using ILRuntime.Reflection;
using ILRuntime.Runtime.Debugger;
using ILRuntime.Runtime.Intepreter;
using ILRuntime.Runtime.Intepreter.OpCodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
namespace ILRuntime.CLR.Method
{
    public class ILMethod : IMethod
    {
        OpCode[] body;
        MethodDefinition def;
        List<IType> parameters;
        ILRuntime.Runtime.Enviorment.AppDomain appdomain;
        ILType declaringType;
        ExceptionHandler[] exceptionHandler;
        KeyValuePair<string, IType>[] genericParameters;
        IType[] genericArguments;
        Dictionary<int, int[]> jumptables;
        bool isDelegateInvoke;
        ILRuntimeMethodInfo refletionMethodInfo;
        ILRuntimeConstructorInfo reflectionCtorInfo;
        int paramCnt, localVarCnt;
        Mono.Collections.Generic.Collection<Mono.Cecil.Cil.VariableDefinition> variables;
        int hashCode = -1;
        static int instance_id = 0x10000000;

        public MethodDefinition Definition { get { return def; } }

        public Dictionary<int, int[]> JumpTables { get { return jumptables; } }

        internal IDelegateAdapter DelegateAdapter { get; set; }

        internal int StartLine { get; set; }

        internal int EndLine { get; set; }

        public MethodInfo ReflectionMethodInfo
        {
            get
            {
                if (IsConstructor)
                    throw new NotSupportedException();
                if (refletionMethodInfo == null)
                    refletionMethodInfo = new ILRuntimeMethodInfo(this);
                return refletionMethodInfo;
            }
        }

        public ConstructorInfo ReflectionConstructorInfo
        {
            get
            {
                if (!IsConstructor)
                    throw new NotSupportedException();
                if (reflectionCtorInfo == null)
                    reflectionCtorInfo = new ILRuntimeConstructorInfo(this);
                return reflectionCtorInfo;
            }
        }

        internal ExceptionHandler[] ExceptionHandler
        {
            get
            {
                if (body == null)
                    InitCodeBody();
                return exceptionHandler;
            }
        }

        public string Name
        {
            get
            {
                return def.Name;
            }
        }

        public IType DeclearingType
        {
            get
            {
                return declaringType;
            }
        }

        public bool HasThis
        {
            get
            {
                return def.HasThis;
            }
        }
        public int GenericParameterCount
        {
            get
            {
                if (IsGenericInstance)
                    return 0;
                return def.GenericParameters.Count;
            }
        }
        public bool IsGenericInstance
        {
            get
            {
                return genericParameters != null;
            }
        }
        public Mono.Collections.Generic.Collection<Mono.Cecil.Cil.VariableDefinition> Variables
        {
            get
            {
                return variables;
            }
        }

        public KeyValuePair<string, IType>[] GenericArguments { get { return genericParameters; } }

        public IType[] GenericArugmentsArray { get { return genericArguments; } }
        public ILMethod(MethodDefinition def, ILType type, ILRuntime.Runtime.Enviorment.AppDomain domain)
        {
            this.def = def;
            declaringType = type;
            if (def.ReturnType.IsGenericParameter)
            {
                ReturnType = FindGenericArgument(def.ReturnType.Name);
            }
            else
                ReturnType = domain.GetType(def.ReturnType, type, this);
            if (type.IsDelegate && def.Name == "Invoke")
                isDelegateInvoke = true;
            appdomain = domain;
            paramCnt = def.HasParameters ? def.Parameters.Count : 0;
#if DEBUG && !DISABLE_ILRUNTIME_DEBUG
            if (def.HasBody)
            {
                Mono.Cecil.Cil.SequencePoint sp = GetValidSequence(0, 1);
                if (sp != null)
                {
                    StartLine = sp.StartLine;
                    sp = GetValidSequence(def.Body.Instructions.Count - 1, -1);
                    if (sp != null)
                    {
                        EndLine = sp.EndLine;
                    }
                }
            }
#endif
        }

        Mono.Cecil.Cil.SequencePoint GetValidSequence(int startIdx, int dir)
        {
            IDictionary<Mono.Cecil.Cil.Instruction, Mono.Cecil.Cil.SequencePoint> seqMapping = def.DebugInformation.GetSequencePointMapping();
            Mono.Cecil.Cil.SequencePoint cur = DebugService.FindSequencePoint(def.Body.Instructions[startIdx], seqMapping);
            while (cur != null && cur.StartLine == 0x0feefee)
            {
                startIdx += dir;
                if (startIdx >= 0 && startIdx < def.Body.Instructions.Count)
                {
                    cur = DebugService.FindSequencePoint(def.Body.Instructions[startIdx], seqMapping);
                }
                else
                    break;
            }

            return cur;
        }

        public IType FindGenericArgument(string name)
        {
            IType res = declaringType.FindGenericArgument(name);
            if (res == null && genericParameters != null)
            {
                foreach (KeyValuePair<string, IType> i in genericParameters)
                {
                    if (i.Key == name)
                        return i.Value;
                }
            }
            else
                return res;
            return null;
        }

        internal OpCode[] Body
        {
            get
            {
                if (body == null)
                    InitCodeBody();
                return body;
            }
        }

        public bool HasBody
        {
            get
            {
                return body != null;
            }
        }

        public int LocalVariableCount
        {
            get
            {
                return localVarCnt;
            }
        }

        public bool IsConstructor
        {
            get
            {
                return def.IsConstructor;
            }
        }

        public bool IsDelegateInvoke
        {
            get
            {
                return isDelegateInvoke;
            }
        }

        public bool IsStatic
        {
            get { return def.IsStatic; }
        }

        public int ParameterCount
        {
            get
            {
                return paramCnt;
            }
        }


        public List<IType> Parameters
        {
            get
            {
                if (def.HasParameters && parameters == null)
                {
                    InitParameters();
                }
                return parameters;
            }
        }

        public IType ReturnType
        {
            get;
            private set;
        }

        public void Prewarm(bool recursive)
        {
            HashSet<ILMethod> alreadyPrewarmed = null;
            if (recursive)
            {
                alreadyPrewarmed = new HashSet<ILMethod>();
            }
            Prewarm(alreadyPrewarmed);
        }

        private void Prewarm(HashSet<ILMethod> alreadyPrewarmed)
        {
            if (alreadyPrewarmed != null && alreadyPrewarmed.Add(this) == false)
                return;
            if (GenericParameterCount > 0 && !IsGenericInstance)
                return;
            //当前方法用到的IType，提前InitializeMethods()。各个子调用，提前InitParameters()
            OpCode[] body = Body;
            //当前方法用到的CLR局部变量，提前InitializeFields()、GetTypeFlags()
            for (int i = 0; i < LocalVariableCount; i++)
            {
                Mono.Cecil.Cil.VariableDefinition v = Variables[i];
                TypeReference vt = v.VariableType;
                IType t;
                if (vt.IsGenericParameter)
                {
                    t = FindGenericArgument(vt.Name);
                }
                else
                {
                    t = appdomain.GetType(v.VariableType, DeclearingType, this);
                }
                if (t is CLRType ct)
                {
                    Dictionary<int, FieldInfo> fields = ct.Fields;
                    ILRuntime.CLR.Utils.Extensions.GetTypeFlags(ct.TypeForCLR);
                }
            }
            foreach (OpCode ins in body)
            {
                switch (ins.Code)
                {
                    case OpCodeEnum.Call:
                    case OpCodeEnum.Newobj:
                    case OpCodeEnum.Ldftn:
                    case OpCodeEnum.Ldvirtftn:
                    case OpCodeEnum.Callvirt:
                        {
                            IMethod m = appdomain.GetMethod(ins.TokenInteger);
                            if (m is ILMethod ilm)
                            {
                                //如果参数alreadyPrewarmed不为空，则不仅prewarm当前方法，还会递归prewarm所有子调用
                                //如果参数alreadyPrewarmed为空，则只prewarm当前方法
                                if (alreadyPrewarmed != null)
                                {
                                    ilm.Prewarm(alreadyPrewarmed);
                                }
                            }
                            else if (m is CLRMethod clrm)
                            {
                                ILRuntime.CLR.Utils.Extensions.GetTypeFlags(clrm.DeclearingType.TypeForCLR);
                            }
                        }
                        break;
                    case OpCodeEnum.Ldfld:
                    case OpCodeEnum.Stfld:
                    case OpCodeEnum.Ldflda:
                    case OpCodeEnum.Ldsfld:
                    case OpCodeEnum.Ldsflda:
                    case OpCodeEnum.Stsfld:
                    case OpCodeEnum.Ldtoken:
                        {
                            //提前InitializeBaseType()
                            IType t = appdomain.GetType((int)(ins.TokenLong >> 32));
                            if (t != null)
                            {
                                IType baseType = t.BaseType;
                            }
                        }
                        break;
                }
            }
        }

        void InitCodeBody()
        {
            if (def.HasBody)
            {
                localVarCnt = def.Body.Variables.Count;
                body = new OpCode[def.Body.Instructions.Count];
                Dictionary<Mono.Cecil.Cil.Instruction, int> addr = new Dictionary<Mono.Cecil.Cil.Instruction, int>();
                for (int i = 0; i < body.Length; i++)
                {
                    Mono.Cecil.Cil.Instruction c = def.Body.Instructions[i];
                    OpCode code = new OpCode();
                    code.Code = (OpCodeEnum)c.OpCode.Code;
                    addr[c] = i;
                    body[i] = code;
                }
                for (int i = 0; i < body.Length; i++)
                {
                    Mono.Cecil.Cil.Instruction c = def.Body.Instructions[i];
                    InitToken(ref body[i], c.Operand, addr);
                    if (i > 0 && c.OpCode.Code == Mono.Cecil.Cil.Code.Callvirt && def.Body.Instructions[i - 1].OpCode.Code == Mono.Cecil.Cil.Code.Constrained)
                    {
                        body[i - 1].TokenLong = body[i].TokenInteger;
                    }
                }

                for (int i = 0; i < def.Body.ExceptionHandlers.Count; i++)
                {
                    Mono.Cecil.Cil.ExceptionHandler eh = def.Body.ExceptionHandlers[i];
                    if (exceptionHandler == null)
                        exceptionHandler = new Method.ExceptionHandler[def.Body.ExceptionHandlers.Count];
                    ExceptionHandler e = new ExceptionHandler();
                    e.HandlerStart = addr[eh.HandlerStart];
                    e.HandlerEnd = addr[eh.HandlerEnd] - 1;
                    e.TryStart = addr[eh.TryStart];
                    e.TryEnd = addr[eh.TryEnd] - 1;
                    switch (eh.HandlerType)
                    {
                        case Mono.Cecil.Cil.ExceptionHandlerType.Catch:
                            e.CatchType = appdomain.GetType(eh.CatchType, declaringType, this);
                            e.HandlerType = ExceptionHandlerType.Catch;
                            break;
                        case Mono.Cecil.Cil.ExceptionHandlerType.Finally:
                            e.HandlerType = ExceptionHandlerType.Finally;
                            break;
                        case Mono.Cecil.Cil.ExceptionHandlerType.Fault:
                            e.HandlerType = ExceptionHandlerType.Fault;
                            break;
                        default:
                            throw new NotImplementedException();
                    }
                    exceptionHandler[i] = e;
                    //Mono.Cecil.Cil.ExceptionHandlerType.
                }
                //Release Method body to save memory
                variables = def.Body.Variables;
                def.Body = null;
            }
            else
                body = new OpCode[0];
        }

        unsafe void InitToken(ref OpCode code, object token, Dictionary<Mono.Cecil.Cil.Instruction, int> addr)
        {
            switch (code.Code)
            {
                case OpCodeEnum.Leave:
                case OpCodeEnum.Leave_S:
                case OpCodeEnum.Br:
                case OpCodeEnum.Br_S:
                case OpCodeEnum.Brtrue:
                case OpCodeEnum.Brtrue_S:
                case OpCodeEnum.Brfalse:
                case OpCodeEnum.Brfalse_S:
                //比较流程控制
                case OpCodeEnum.Beq:
                case OpCodeEnum.Beq_S:
                case OpCodeEnum.Bne_Un:
                case OpCodeEnum.Bne_Un_S:
                case OpCodeEnum.Bge:
                case OpCodeEnum.Bge_S:
                case OpCodeEnum.Bge_Un:
                case OpCodeEnum.Bge_Un_S:
                case OpCodeEnum.Bgt:
                case OpCodeEnum.Bgt_S:
                case OpCodeEnum.Bgt_Un:
                case OpCodeEnum.Bgt_Un_S:
                case OpCodeEnum.Ble:
                case OpCodeEnum.Ble_S:
                case OpCodeEnum.Ble_Un:
                case OpCodeEnum.Ble_Un_S:
                case OpCodeEnum.Blt:
                case OpCodeEnum.Blt_S:
                case OpCodeEnum.Blt_Un:
                case OpCodeEnum.Blt_Un_S:
                    code.TokenInteger = addr[(Mono.Cecil.Cil.Instruction)token];
                    break;
                case OpCodeEnum.Ldc_I4:
                    code.TokenInteger = (int)token;
                    break;
                case OpCodeEnum.Ldc_I4_S:
                    code.TokenInteger = (sbyte)token;
                    break;
                case OpCodeEnum.Ldc_I8:
                    code.TokenLong = (long)token;
                    break;
                case OpCodeEnum.Ldc_R4:
                    {
                        float val = (float)token;
                        code.TokenInteger = *(int*)&val;
                    }
                    break;
                case OpCodeEnum.Ldc_R8:
                    {
                        double val = (double)token;
                        code.TokenLong = *(long*)&val;
                    }
                    break;
                case OpCodeEnum.Stloc:
                case OpCodeEnum.Stloc_S:
                case OpCodeEnum.Ldloc:
                case OpCodeEnum.Ldloc_S:
                case OpCodeEnum.Ldloca:
                case OpCodeEnum.Ldloca_S:
                    {
                        Mono.Cecil.Cil.VariableDefinition vd = (Mono.Cecil.Cil.VariableDefinition)token;
                        code.TokenInteger = vd.Index;
                    }
                    break;
                case OpCodeEnum.Ldarg_S:
                case OpCodeEnum.Ldarg:
                case OpCodeEnum.Ldarga:
                case OpCodeEnum.Ldarga_S:
                case OpCodeEnum.Starg:
                case OpCodeEnum.Starg_S:
                    {
                        Mono.Cecil.ParameterDefinition vd = (Mono.Cecil.ParameterDefinition)token;
                        code.TokenInteger = vd.Index;
                        if (HasThis)
                            code.TokenInteger++;
                    }
                    break;
                case OpCodeEnum.Call:
                case OpCodeEnum.Newobj:
                case OpCodeEnum.Ldftn:
                case OpCodeEnum.Ldvirtftn:
                case OpCodeEnum.Callvirt:
                    {
                        IMethod m = appdomain.GetMethod(token, declaringType, this, out bool invalidToken);
                        if (m != null)
                        {
                            if (code.Code == OpCodeEnum.Callvirt && m is ILMethod)
                            {
                                ILMethod ilm = (ILMethod)m;
                                if (!ilm.def.IsAbstract && !ilm.def.IsVirtual && !ilm.DeclearingType.IsInterface)
                                    code.Code = OpCodeEnum.Call;
                            }
                            if (invalidToken)
                                code.TokenInteger = m.GetHashCode();
                            else
                                code.TokenInteger = token.GetHashCode();
                        }
                        else
                        {
                            //Cannot find method or the method is dummy
                            MethodReference _ref = (MethodReference)token;
                            int paramCnt = _ref.HasParameters ? _ref.Parameters.Count : 0;
                            if (_ref.HasThis)
                                paramCnt++;
                            code.TokenLong = paramCnt;
                        }
                    }
                    break;
                case OpCodeEnum.Constrained:
                case OpCodeEnum.Box:
                case OpCodeEnum.Unbox_Any:
                case OpCodeEnum.Unbox:
                case OpCodeEnum.Initobj:
                case OpCodeEnum.Isinst:
                case OpCodeEnum.Newarr:
                case OpCodeEnum.Stobj:
                case OpCodeEnum.Ldobj:
                    {
                        code.TokenInteger = GetTypeTokenHashCode(token);
                    }
                    break;
                case OpCodeEnum.Stfld:
                case OpCodeEnum.Ldfld:
                case OpCodeEnum.Ldflda:
                    {
                        code.TokenLong = appdomain.GetStaticFieldIndex(token, declaringType, this);
                    }
                    break;

                case OpCodeEnum.Stsfld:
                case OpCodeEnum.Ldsfld:
                case OpCodeEnum.Ldsflda:
                    {
                        code.TokenLong = appdomain.GetStaticFieldIndex(token, declaringType, this);
                    }
                    break;
                case OpCodeEnum.Ldstr:
                    {
                        long hashCode = appdomain.CacheString(token);
                        code.TokenLong = hashCode;
                    }
                    break;
                case OpCodeEnum.Ldtoken:
                    {
                        if (token is FieldReference)
                        {
                            code.TokenInteger = 0;
                            code.TokenLong = appdomain.GetStaticFieldIndex(token, declaringType, this);
                        }
                        else if (token is TypeReference)
                        {
                            code.TokenInteger = 1;
                            code.TokenLong = GetTypeTokenHashCode(token);
                        }
                        else
                            throw new NotImplementedException();
                    }
                    break;
                case OpCodeEnum.Switch:
                    {
                        PrepareJumpTable(token, addr);
                        code.TokenInteger = token.GetHashCode();
                    }
                    break;
            }
        }

        int GetTypeTokenHashCode(object token)
        {
            IType t = appdomain.GetType(token, declaringType, this);
            bool isGenericParameter = CheckHasGenericParamter(token);
            if (t == null && isGenericParameter)
            {
                t = FindGenericArgument(((TypeReference)token).Name);
            }
            if (t != null)
            {
                if (t is ILType || isGenericParameter)
                {
                    return t.GetHashCode();
                }
                else
                    return token.GetHashCode();
            }
            return 0;
        }

        bool CheckHasGenericParamter(object token)
        {
            if (token is TypeReference)
            {
                TypeReference _ref = ((TypeReference)token);
                if (_ref.IsArray)
                    return CheckHasGenericParamter(((ArrayType)_ref).ElementType);
                if (_ref.IsGenericParameter)
                    return true;
                if (_ref.IsGenericInstance)
                {
                    GenericInstanceType gi = (GenericInstanceType)_ref;
                    foreach (TypeReference i in gi.GenericArguments)
                    {
                        if (CheckHasGenericParamter(i))
                            return true;
                    }
                    return false;
                }
                else
                    return false;
            }
            else
                return false;
        }

        void PrepareJumpTable(object token, Dictionary<Mono.Cecil.Cil.Instruction, int> addr)
        {
            int hashCode = token.GetHashCode();

            if (jumptables == null)
                jumptables = new Dictionary<int, int[]>();
            if (jumptables.ContainsKey(hashCode))
                return;
            Mono.Cecil.Cil.Instruction[] e = token as Mono.Cecil.Cil.Instruction[];
            int[] addrs = new int[e.Length];
            for (int i = 0; i < e.Length; i++)
            {
                addrs[i] = addr[e[i]];
            }

            jumptables[hashCode] = addrs;
        }

        void InitParameters()
        {
            parameters = new List<IType>();
            foreach (ParameterDefinition i in def.Parameters)
            {
                IType type = null;
                bool isByRef = false;
                bool isArray = false;
                int rank = 1;
                TypeReference pt = i.ParameterType;
                if (pt.IsByReference)
                {
                    isByRef = true;
                    pt = ((ByReferenceType)pt).ElementType;
                }
                if (pt.IsArray)
                {
                    isArray = true;
                    rank = ((ArrayType)pt).Rank;
                    pt = ((ArrayType)pt).ElementType;
                }
                if (pt.IsGenericParameter)
                {
                    type = FindGenericArgument(pt.Name);
                    if (type == null && def.HasGenericParameters)
                    {
                        bool found = false;
                        foreach (GenericParameter j in def.GenericParameters)
                        {
                            if (j.Name == pt.Name)
                            {
                                found = true;
                                break;
                            }
                        }
                        if (found)
                        {
                            type = new ILGenericParameterType(pt.Name);
                        }
                        else
                            throw new NotSupportedException("Cannot find Generic Parameter " + pt.Name + " in " + def.FullName);
                    }
                }
                else
                    type = appdomain.GetType(pt, declaringType, this);

                if (isArray)
                    type = type.MakeArrayType(rank);
                if (isByRef)
                    type = type.MakeByRefType();
                parameters.Add(type);
            }
        }

        public IMethod MakeGenericMethod(IType[] genericArguments)
        {
            KeyValuePair<string, IType>[] genericParameters = new KeyValuePair<string, IType>[genericArguments.Length];
            for (int i = 0; i < genericArguments.Length; i++)
            {
                string name = def.GenericParameters[i].Name;
                IType val = genericArguments[i];
                genericParameters[i] = new KeyValuePair<string, IType>(name, val);
            }

            ILMethod m = new ILMethod(def, declaringType, appdomain);
            m.genericParameters = genericParameters;
            m.genericArguments = genericArguments;
            if (m.def.ReturnType.IsGenericParameter)
            {
                m.ReturnType = m.FindGenericArgument(m.def.ReturnType.Name);
            }
            return m;
        }

        string cachedName;
        public override string ToString()
        {
            if (cachedName == null)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(declaringType.FullName);
                sb.Append('.');
                sb.Append(Name);
                sb.Append('(');
                bool isFirst = true;
                if (parameters == null)
                    InitParameters();
                for (int i = 0; i < parameters.Count; i++)
                {
                    if (isFirst)
                        isFirst = false;
                    else
                        sb.Append(", ");
                    sb.Append(parameters[i].Name);
                    sb.Append(' ');
                    sb.Append(def.Parameters[i].Name);
                }
                sb.Append(')');
                cachedName = sb.ToString();
            }
            return cachedName;
        }

        public override int GetHashCode()
        {
            if (hashCode == -1)
                hashCode = System.Threading.Interlocked.Add(ref instance_id, 1);
            return hashCode;
        }
    }
}
