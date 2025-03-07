﻿using ILRuntime.CLR.Method;
using ILRuntime.Mono.Cecil;
using ILRuntime.Reflection;
using ILRuntime.Runtime.Enviorment;
using ILRuntime.Runtime.Stack;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ILRuntime.CLR.TypeSystem
{
    public unsafe class CLRType : IType
    {
        Type clrType;
        bool isPrimitive, isValueType, isEnum;
        Dictionary<string, List<CLRMethod>> methods;
        ILRuntime.Runtime.Enviorment.AppDomain appdomain;
        List<CLRMethod> constructors;
        KeyValuePair<string, IType>[] genericArguments;
        List<CLRType> genericInstances;
        Dictionary<string, int> fieldMapping;
        Dictionary<int, FieldInfo> fieldInfoCache;
        Dictionary<int, CLRFieldGetterDelegate> fieldGetterCache;
        Dictionary<int, CLRFieldSetterDelegate> fieldSetterCache;
        Dictionary<int, KeyValuePair<CLRFieldBindingDelegate, CLRFieldBindingDelegate>> fieldBindingCache;

        Dictionary<int, int> fieldIdxMapping;
        IType[] orderedFieldTypes;

        CLRMemberwiseCloneDelegate memberwiseCloneDelegate;
        CLRCreateDefaultInstanceDelegate createDefaultInstanceDelegate;
        CLRCreateArrayInstanceDelegate createArrayInstanceDelegate;
        Dictionary<int, int> fieldTokenMapping;
        IType byRefType, elementType;
        Dictionary<int, IType> arrayTypes;
        IType[] interfaces;
        bool isDelegate;
        IType baseType;
        bool isBaseTypeInitialized = false, interfaceInitialized = false, valueTypeBinderGot = false;
        ILRuntimeWrapperType wraperType;
        ValueTypeBinder valueTypeBinder;

        int hashCode = -1;
        static int instance_id = 0x20000000;

        public Dictionary<int, FieldInfo> Fields
        {
            get
            {
                if (fieldMapping == null)
                    InitializeFields();
                return fieldInfoCache;
            }
        }

        public Dictionary<int, int> FieldIndexMapping
        {
            get { return fieldIdxMapping; }
        }

        public IType[] OrderedFieldTypes
        {
            get
            {
                if (fieldMapping == null)
                    InitializeFields();
                return orderedFieldTypes;
            }
        }

        public int TotalFieldCount
        {
            get
            {
                if (fieldMapping == null)
                    InitializeFields();

                if (fieldIdxMapping != null)
                    return fieldIdxMapping.Count;
                else
                    throw new NotSupportedException("Cannot find ValueTypeBinder for type:" + clrType.FullName);
            }
        }

        public ILRuntime.Runtime.Enviorment.AppDomain AppDomain
        {
            get
            {
                return appdomain;
            }
        }

        public CLRType(Type clrType, Runtime.Enviorment.AppDomain appdomain)
        {
            this.clrType = clrType;
            this.appdomain = appdomain;
            isPrimitive = clrType.IsPrimitive;
            isEnum = clrType.IsEnum;
            isValueType = clrType.IsValueType;
            isDelegate = clrType.BaseType == typeof(MulticastDelegate);
        }

        public bool IsGenericInstance
        {
            get
            {
                return genericArguments != null;
            }
        }

        public KeyValuePair<string, IType>[] GenericArguments
        {
            get
            {
                return genericArguments;
            }
        }

        public IType ElementType { get { return elementType; } }

        public bool HasGenericParameter
        {
            get
            {
                if (genericArguments != null)
                {
                    foreach (KeyValuePair<string, IType> i in genericArguments)
                    {
                        if (i.Value is ILType && i.Value.HasGenericParameter)
                        {
                            return true;
                        }
                    }
                }
                return clrType.ContainsGenericParameters;
            }
        }

        public bool IsGenericParameter
        {
            get
            {
                return clrType.IsGenericParameter;
            }
        }

        public bool IsInterface
        {
            get { return clrType.IsInterface; }
        }

        public Type TypeForCLR
        {
            get
            {
                return clrType;
            }
        }

        public Type ReflectionType
        {
            get
            {
                if (wraperType == null)
                    wraperType = new ILRuntimeWrapperType(this);
                return wraperType;
            }
        }
        public IType ByRefType
        {
            get
            {
                return byRefType;
            }
        }
        public IType ArrayType
        {
            get
            {
                return arrayTypes != null ? arrayTypes[1] : null;
            }
        }

        public bool IsArray
        {
            get; private set;
        }

        public int ArrayRank
        {
            get; private set;
        }

        public bool IsValueType
        {
            get
            {
                return isValueType;
            }
        }

        public bool IsByRef
        {
            get
            {
                return clrType.IsByRef;
            }
        }

        public bool IsDelegate
        {
            get
            {
                return isDelegate;
            }
        }

        public bool IsPrimitive
        {
            get
            {
                return isPrimitive;
            }
        }

        public bool IsEnum
        {
            get
            {
                return isEnum;
            }
        }
        public string FullName
        {
            get
            {
                return clrType.FullName;
            }
        }
        public string Name
        {
            get
            {
                return clrType.Name;
            }
        }

        public IType BaseType
        {
            get
            {
                if (!isBaseTypeInitialized)
                    InitializeBaseType();
                return baseType;
            }
        }

        public IType[] Implements
        {
            get
            {
                if (!interfaceInitialized)
                    InitializeInterfaces();
                return interfaces;
            }
        }

        public ValueTypeBinder ValueTypeBinder
        {
            get
            {
                if (clrType.IsValueType)
                {
                    if (!valueTypeBinderGot)
                    {
                        valueTypeBinderGot = true;
                        appdomain.ValueTypeBinders.TryGetValue(clrType, out valueTypeBinder);
                    }
                    return valueTypeBinder;
                }
                else
                    return null;
            }
        }

        public object PerformMemberwiseClone(object target)
        {
            if (memberwiseCloneDelegate == null)
            {
                if (!AppDomain.MemberwiseCloneMap.TryGetValue(clrType, out memberwiseCloneDelegate))
                {
                    MethodInfo memberwiseClone = clrType.GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                    if (memberwiseClone != null)
                    {
                        memberwiseCloneDelegate = (ref object t) => memberwiseClone.Invoke(t, null);
                    }
                    else
                    {
                        throw new InvalidOperationException("Memberwise clone method not found for " + clrType.FullName);
                    }
                }
            }

            return memberwiseCloneDelegate(ref target);
        }

        void InitializeBaseType()
        {
            baseType = appdomain.GetType(clrType.BaseType);
            if (baseType.TypeForCLR == typeof(Enum) || baseType.TypeForCLR == typeof(object) || baseType.TypeForCLR == typeof(ValueType) || baseType.TypeForCLR == typeof(System.Enum))
            {//都是这样，无所谓
                baseType = null;
            }
            isBaseTypeInitialized = true;
        }

        void InitializeInterfaces()
        {
            interfaceInitialized = true;
            Type[] arr = clrType.GetInterfaces();
            if (arr.Length > 0)
            {
                interfaces = new IType[arr.Length];
                for (int i = 0; i < interfaces.Length; i++)
                {
                    interfaces[i] = appdomain.GetType(arr[i]);
                }
            }
        }

        public object GetFieldValue(int hash, object target)
        {
            if (fieldMapping == null)
                InitializeFields();

            CLRFieldGetterDelegate getter = GetFieldGetter(hash);
            if (getter != null)
            {
                return getter(ref target);
            }

            FieldInfo fieldinfo = GetField(hash);
            if (fieldinfo != null)
            {
                return fieldinfo.GetValue(target);
            }

            return null;
        }

        public bool CopyFieldToStack(int hash, object target, Runtime.Intepreter.ILIntepreter intp, ref StackObject* esp, IList<object> mStack)
        {
            if (fieldMapping == null)
                InitializeFields();
            if (fieldBindingCache == null)
                return false;
            KeyValuePair<CLRFieldBindingDelegate, CLRFieldBindingDelegate> binding = GetFieldBinding(hash);
            if (binding.Key != null)
            {
                esp = binding.Key(ref target, intp, esp, mStack);
                return true;
            }
            else
                return false;
        }

        public bool AssignFieldFromStack(int hash, ref object target, Runtime.Intepreter.ILIntepreter intp, StackObject* esp, IList<object> mStack)
        {
            if (fieldMapping == null)
                InitializeFields();
            if (fieldBindingCache == null)
                return false;
            KeyValuePair<CLRFieldBindingDelegate, CLRFieldBindingDelegate> binding = GetFieldBinding(hash);
            if (binding.Value != null)
            {
                esp = binding.Value(ref target, intp, esp, mStack);
                return true;
            }
            else
                return false;
        }

        public void SetStaticFieldValue(int hash, object value)
        {
            if (fieldMapping == null)
                InitializeFields();

            CLRFieldSetterDelegate setter = GetFieldSetter(hash);
            object target = null;
            if (setter != null)
            {
                setter(ref target, value);
                return;
            }

            FieldInfo fieldInfo = GetField(hash);
            if (fieldInfo != null)
            {
                fieldInfo.SetValue(null, value);
            }
        }

        public unsafe void SetFieldValue(int hash, ref object target, object value, bool directSet = false)
        {
            if (fieldMapping == null)
                InitializeFields();

            if (!directSet)
            {
                CLRFieldSetterDelegate setter = GetFieldSetter(hash);
                if (setter != null)
                {
                    setter(ref target, value);
                    return;
                }
            }
            FieldInfo fieldInfo = GetField(hash);
            if (fieldInfo != null)
            {
                fieldInfo.SetValue(target, value);
            }
        }

        KeyValuePair<CLRFieldBindingDelegate, CLRFieldBindingDelegate> GetFieldBinding(int hash)
        {
            Dictionary<int, KeyValuePair<CLRFieldBindingDelegate, CLRFieldBindingDelegate>> dic = fieldBindingCache;
            if (dic != null && dic.TryGetValue(hash, out KeyValuePair<CLRFieldBindingDelegate, CLRFieldBindingDelegate> res))
                return res;
            else if (BaseType != null)
                return ((CLRType)BaseType).GetFieldBinding(hash);
            else
                return default(KeyValuePair<CLRFieldBindingDelegate, CLRFieldBindingDelegate>);
        }

        private CLRFieldGetterDelegate GetFieldGetter(int hash)
        {
            Dictionary<int, CLRFieldGetterDelegate> dic = fieldGetterCache;
            if (dic != null && dic.TryGetValue(hash, out CLRFieldGetterDelegate res))
                return res;
            else if (BaseType != null)
                return ((CLRType)BaseType).GetFieldGetter(hash);
            else
                return null;
        }

        private CLRFieldSetterDelegate GetFieldSetter(int hash)
        {
            Dictionary<int, CLRFieldSetterDelegate> dic = fieldSetterCache;
            if (dic != null && dic.TryGetValue(hash, out CLRFieldSetterDelegate res))
                return res;
            else if (BaseType != null)
                return ((CLRType)BaseType).GetFieldSetter(hash);
            else
                return null;
        }

        public FieldInfo GetField(int hash)
        {
            Dictionary<int, FieldInfo> dic = Fields;
            if (dic.TryGetValue(hash, out FieldInfo res))
                return res;
            else if (BaseType != null)
                return ((CLRType)BaseType).GetField(hash);
            else
                return null;
        }

        void InitializeMethods()
        {
            methods = new Dictionary<string, List<CLRMethod>>();
            constructors = new List<CLRMethod>();
            foreach (MethodInfo i in clrType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (i.IsPrivate)
                    continue;
                if (!methods.TryGetValue(i.Name, out List<CLRMethod> lst))
                {
                    lst = new List<CLRMethod>();
                    methods[i.Name] = lst;
                }
                lst.Add(new CLRMethod(i, this, appdomain));
            }
            foreach (ConstructorInfo i in clrType.GetConstructors())
            {
                constructors.Add(new CLRMethod(i, this, appdomain));
            }
        }
        public List<IMethod> GetMethods()
        {
            if (methods == null)
                InitializeMethods();
            List<IMethod> res = new List<IMethod>();
            foreach (KeyValuePair<string, List<CLRMethod>> i in methods)
            {
                foreach (CLRMethod j in i.Value)
                    res.Add(j);
            }

            return res;
        }

        public IMethod GetVirtualMethod(IMethod method)
        {
            IMethod m = GetMethod(method.Name, method.Parameters, null, method.ReturnType);
            if (m == null)
            {
                return method;
            }
            else
                return m;
        }

        void InitializeFields()
        {
            fieldMapping = new Dictionary<string, int>();
            fieldInfoCache = new Dictionary<int, FieldInfo>();

            FieldInfo[] fields = clrType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Static);
            int idx = 0;
            bool hasValueTypeBinder = ValueTypeBinder != null;
            if (hasValueTypeBinder)
            {
                fieldIdxMapping = new Dictionary<int, int>();
            }
            if (hasValueTypeBinder || isEnum)
            {
                orderedFieldTypes = new IType[fields.Length];
            }
            foreach (FieldInfo i in fields)
            {
                int hashCode = i.GetHashCode();

                if (i.IsPublic || i.IsFamily || hasValueTypeBinder)
                {
                    fieldMapping[i.Name] = hashCode;
                    fieldInfoCache[hashCode] = i;
                }
                if ((hasValueTypeBinder || isEnum) && !i.IsStatic)
                {
                    orderedFieldTypes[idx] = appdomain.GetType(i.FieldType);
                    if (hasValueTypeBinder)
                        fieldIdxMapping[hashCode] = idx++;
                    else
                        idx++;
                }

                if (AppDomain.FieldGetterMap.TryGetValue(i, out CLRFieldGetterDelegate getter))
                {
                    if (fieldGetterCache == null) fieldGetterCache = new Dictionary<int, CLRFieldGetterDelegate>();
                    fieldGetterCache[hashCode] = getter;
                }

                if (AppDomain.FieldSetterMap.TryGetValue(i, out CLRFieldSetterDelegate setter))
                {
                    if (fieldSetterCache == null) fieldSetterCache = new Dictionary<int, CLRFieldSetterDelegate>();
                    fieldSetterCache[hashCode] = setter;
                }

                if (AppDomain.FieldBindingMap.TryGetValue(i, out KeyValuePair<CLRFieldBindingDelegate, CLRFieldBindingDelegate> binding))
                {
                    if (fieldBindingCache == null) fieldBindingCache = new Dictionary<int, KeyValuePair<CLRFieldBindingDelegate, CLRFieldBindingDelegate>>();
                    fieldBindingCache[hashCode] = binding;
                }
            }
            if (orderedFieldTypes != null)
            {
                Array.Resize(ref orderedFieldTypes, idx);
            }
        }
        public int GetFieldIndex(object token)
        {
            if (fieldMapping == null)
                InitializeFields();
            int hashCode = token.GetHashCode();
            if (fieldTokenMapping == null)
                fieldTokenMapping = new Dictionary<int, int>();
            if (fieldTokenMapping.TryGetValue(hashCode, out int idx))
                return idx;
            FieldReference f = token as FieldReference;
            if (fieldMapping.TryGetValue(f.Name, out idx))
            {
                fieldTokenMapping[hashCode] = idx;
                return idx;
            }

            return -1;
        }
        public IType FindGenericArgument(string key)
        {
            if (genericArguments != null)
            {
                foreach (KeyValuePair<string, IType> i in genericArguments)
                {
                    if (i.Key == key)
                        return i.Value;
                }
            }
            return null;
        }
        public IMethod GetMethod(string name, int paramCount, bool declaredOnly = false)
        {
            if (methods == null)
                InitializeMethods();
            if (methods.TryGetValue(name, out List<CLRMethod> lst))
            {
                foreach (CLRMethod i in lst)
                {
                    if (i.ParameterCount == paramCount)
                        return i;
                }
            }
            return null;
        }

        bool MatchGenericParameters(Type[] args, Type type, Type q, IType[] genericArguments)
        {
            if (type.IsGenericParameter)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == type)
                    {
                        return q == genericArguments[i].TypeForCLR;
                    }
                }
                throw new NotSupportedException();
            }
            else
            {
                if (type.IsArray)
                {
                    if (q.IsArray)
                    {
                        return MatchGenericParameters(args, type.GetElementType(), q.GetElementType(), genericArguments);
                    }
                    else
                        return false;
                }
                else if (type.IsByRef)
                {
                    if (q.IsByRef)
                    {
                        return MatchGenericParameters(args, type.GetElementType(), q.GetElementType(), genericArguments);
                    }
                    else
                        return false;
                }
                else if (type.IsGenericType)
                {
                    if (q.IsGenericType)
                    {
                        Type t1 = type.GetGenericTypeDefinition();
                        Type t2 = q.GetGenericTypeDefinition();
                        if (t1 == t2)
                        {
                            Type[] argA = type.GetGenericArguments();
                            Type[] argB = q.GetGenericArguments();
                            if (argA.Length == argB.Length)
                            {
                                for (int i = 0; i < argA.Length; i++)
                                {
                                    if (!MatchGenericParameters(args, argA[i], argB[i], genericArguments))
                                        return false;
                                }
                                return true;
                            }
                            else
                                return false;
                        }
                        else
                            return false;
                    }
                    else
                        return false;
                }
                else
                    return type == q;
            }
        }

        public IMethod GetMethod(string name, List<IType> param, IType[] genericArguments, IType returnType = null, bool declaredOnly = false)
        {
            if (methods == null)
                InitializeMethods();
            IMethod genericMethod = null;
            if (methods.TryGetValue(name, out List<CLRMethod> lst))
            {
                foreach (CLRMethod i in lst)
                {
                    if (i.ParameterCount == param.Count)
                    {
                        bool match = true;
                        if (genericArguments != null && i.GenericParameterCount == genericArguments.Length)
                        {
                            for (int j = 0; j < param.Count; j++)
                            {
                                Type p = i.Parameters[j].TypeForCLR;
                                Type q = param[j].TypeForCLR;

                                if (i.Parameters[j].HasGenericParameter)
                                {
                                    //TODO should match the generic parameters;
                                    if (!MatchGenericParameters(i.GenericArgumentsCLR, i.ParametersCLR[j].ParameterType, q, genericArguments))
                                    {
                                        match = false;
                                        break;
                                    }
                                    else
                                        continue;
                                }
                                if (q != p)
                                {
                                    match = false;
                                    break;
                                }
                            }
                            if (match && genericMethod == null)
                            {
                                genericMethod = i;
                            }
                        }
                        else
                        {
                            if (genericArguments == null)
                                match = i.GenericArguments == null;
                            else
                            {
                                if (i.GenericArguments == null)
                                    match = false;
                                else
                                    match = i.GenericArguments.Length == genericArguments.Length;
                            }
                            if (!match)
                                continue;
                            for (int j = 0; j < param.Count; j++)
                            {
                                Type typeA = param[j].TypeForCLR.IsByRef ? param[j].TypeForCLR.GetElementType() : param[j].TypeForCLR;
                                Type typeB = i.Parameters[j].TypeForCLR.IsByRef ? i.Parameters[j].TypeForCLR.GetElementType() : i.Parameters[j].TypeForCLR;

                                if (typeA != typeB)
                                {
                                    match = false;
                                    break;
                                }
                            }
                            if (match)
                            {
                                try
                                {
                                    match = returnType == null || (i.ReturnType != null && i.ReturnType.TypeForCLR == returnType.TypeForCLR);
                                }
                                catch
                                {

                                }
                            }
                            if (match)
                            {

                                if (i.IsGenericInstance)
                                {
                                    if (i.GenericArguments.Length == genericArguments.Length)
                                    {
                                        for (int j = 0; j < genericArguments.Length; j++)
                                        {
                                            if (i.GenericArguments[j] != genericArguments[j])
                                            {
                                                match = false;
                                                break;
                                            }
                                        }
                                        if (match)
                                            return i;
                                    }
                                }
                                else
                                    return i;
                            }
                        }
                    }
                }
            }
            if (genericArguments != null && genericMethod != null)
            {
                IMethod m = genericMethod.MakeGenericMethod(genericArguments);
                lst.Add((CLRMethod)m);
                return m;
            }
            return null;
        }
        public bool CanAssignTo(IType type)
        {
            if (this == type)
            {
                return true;
            }
            else
            {
                if (type is ILType)
                    return false;
                Type cT = type != null ? type.TypeForCLR : typeof(object);
                return TypeForCLR.IsAssignableFrom(cT);
            }
        }

        public IMethod GetConstructor(List<IType> param)
        {
            if (constructors == null)
                InitializeMethods();
            foreach (CLRMethod i in constructors)
            {
                if (i.ParameterCount == param.Count)
                {
                    bool match = true;

                    for (int j = 0; j < param.Count; j++)
                    {
                        if (param[j].TypeForCLR != i.Parameters[j].TypeForCLR)
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        return i;
                    }
                }
            }

            return null;
        }

        public IType MakeGenericInstance(KeyValuePair<string, IType>[] genericArguments)
        {
            lock (this)
            {
                if (genericInstances == null)
                    genericInstances = new List<CLRType>();
                foreach (CLRType i in genericInstances)
                {
                    bool match = true;
                    for (int j = 0; j < genericArguments.Length; j++)
                    {
                        if (i.genericArguments[j].Value != genericArguments[j].Value)
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                        return i;
                }
                Type[] args = new Type[genericArguments.Length];
                for (int i = 0; i < genericArguments.Length; i++)
                {
                    args[i] = genericArguments[i].Value.TypeForCLR;
                }

                Type newType = null;
#if UNITY_EDITOR || (DEBUG && !DISABLE_ILRUNTIME_DEBUG)
                try
                {
#endif
                    newType = clrType.MakeGenericType(args);
#if UNITY_EDITOR || (DEBUG && !DISABLE_ILRUNTIME_DEBUG)
                }
                catch (Exception)
                {
                    string argString = "";
                    for (int i = 0; i < args.Length; i++)
                    {
                        argString += args[i].FullName + ", ";
                    }

                    argString = argString.Substring(0, argString.Length - 2);
                    throw new Exception($"MakeGenericType failed : {clrType.FullName}<{argString}>");
                }
#endif
                CLRType res = new CLRType(newType, appdomain);
                res.genericArguments = genericArguments;

                genericInstances.Add(res);
                return res;
            }
        }

        public object CreateDefaultInstance()
        {
            if (createDefaultInstanceDelegate == null)
            {
                if (!AppDomain.CreateDefaultInstanceMap.TryGetValue(clrType, out createDefaultInstanceDelegate))
                {
                    createDefaultInstanceDelegate = () => Activator.CreateInstance(TypeForCLR);
                }
            }

            return createDefaultInstanceDelegate();
        }

        public object CreateArrayInstance(int size)
        {
            if (createArrayInstanceDelegate == null)
            {
                if (!AppDomain.CreateArrayInstanceMap.TryGetValue(clrType, out createArrayInstanceDelegate))
                {
                    createArrayInstanceDelegate = s => Array.CreateInstance(TypeForCLR, s);
                }
            }

            return createArrayInstanceDelegate(size);
        }

        public IType MakeByRefType()
        {
            if (byRefType == null)
            {
                Type t = clrType.MakeByRefType();
                byRefType = new CLRType(t, appdomain);
                ((CLRType)byRefType).elementType = this;
            }
            return byRefType;
        }
        public IType MakeArrayType(int rank)
        {
            if (arrayTypes == null)
            {
                arrayTypes = new Dictionary<int, IType>();
            }
            if (!arrayTypes.TryGetValue(rank, out IType atype))
            {
                Type t = rank > 1 ? clrType.MakeArrayType(rank) : clrType.MakeArrayType();
                atype = new CLRType(t, appdomain);
                ((CLRType)atype).elementType = this;
                ((CLRType)atype).IsArray = true;
                ((CLRType)atype).ArrayRank = rank;
                arrayTypes[rank] = atype;
            }
            return atype;
        }

        public IType ResolveGenericType(IType contextType)
        {
            throw new NotImplementedException();
        }

        public override int GetHashCode()
        {
            if (hashCode == -1)
                hashCode = System.Threading.Interlocked.Add(ref instance_id, 1);
            return hashCode;
        }

        public override string ToString()
        {
            return clrType.ToString();
        }
    }
}
