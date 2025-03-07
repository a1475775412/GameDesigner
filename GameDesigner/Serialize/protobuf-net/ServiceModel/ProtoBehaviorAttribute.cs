﻿//#if UNITY_STANDALONE || UNITY_ANDROID || UNITY_IOS || SERVICE && PLAT_XMLSERIALIZER
//using System;
//using System.ServiceModel.Description;
//using System.ServiceModel.Dispatcher;

//namespace ProtoBuf.ServiceModel
//{
//    /// <summary>
//    /// Uses protocol buffer serialization on the specified operation; note that this
//    /// must be enabled on both the client and server.
//    /// </summary>
//    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
//    public sealed class ProtoBehaviorAttribute : Attribute, IOperationBehavior
//    {
//        void IOperationBehavior.AddBindingParameters(OperationDescription operationDescription, System.ServiceModel.Channels.BindingParameterCollection bindingParameters)
//        { }

//        void IOperationBehavior.ApplyClientBehavior(OperationDescription operationDescription, ClientOperation clientOperation)
//        {
//            IOperationBehavior innerBehavior = new ProtoOperationBehavior(operationDescription);
//            innerBehavior.ApplyClientBehavior(operationDescription, clientOperation);
//        }

//        void IOperationBehavior.ApplyDispatchBehavior(OperationDescription operationDescription, DispatchOperation dispatchOperation)
//        {
//            IOperationBehavior innerBehavior = new ProtoOperationBehavior(operationDescription);
//            innerBehavior.ApplyDispatchBehavior(operationDescription, dispatchOperation);
//        }

//        void IOperationBehavior.Validate(OperationDescription operationDescription)
//        { }
//    }
//}
//#endif