﻿using ILRuntime.CLR.TypeSystem;
using ILRuntime.CLR.Utils;
using ILRuntime.Mono.Cecil;
using ILRuntime.Runtime;
using ILRuntime.Runtime.Enviorment;
using ILRuntime.Runtime.Intepreter;
using ILRuntime.Runtime.Stack;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ILRuntime.Reflection
{
    public class ILRuntimeFieldInfo : FieldInfo
    {
        System.Reflection.FieldAttributes attr;
        ILRuntimeType dType;
        ILType ilType;
        IType fieldType;
        bool isStatic;
        int fieldIdx;
        string name;
        FieldDefinition definition;
        Runtime.Enviorment.AppDomain appdomain;
        Attribute[] customAttributes;
        Type[] attributeTypes;

        public IType ILFieldType { get { return fieldType; } }

        public ILRuntimeFieldInfo(FieldDefinition def, ILRuntimeType declaredType, bool isStatic, int fieldIdx)
        {
            definition = def;
            name = def.Name;
            dType = declaredType;
            ilType = dType.ILType;
            appdomain = ilType.AppDomain;
            this.isStatic = isStatic;
            this.fieldIdx = fieldIdx;
            if (isStatic)
                attr |= System.Reflection.FieldAttributes.Static;
            if (def.IsPublic)
            {
                attr |= System.Reflection.FieldAttributes.Public;
            }
            else
                attr |= System.Reflection.FieldAttributes.Private;
            fieldType = isStatic ? ilType.StaticFieldTypes[fieldIdx] : ilType.FieldTypes[fieldIdx];
        }

        public ILRuntimeFieldInfo(FieldDefinition def, ILRuntimeType declaredType, int fieldIdx, IType fieldType)
        {
            definition = def;
            name = def.Name;
            dType = declaredType;
            ilType = dType.ILType;
            appdomain = ilType.AppDomain;
            isStatic = false;
            this.fieldIdx = fieldIdx;
            if (isStatic)
                attr |= System.Reflection.FieldAttributes.Static;
            if (def.IsPublic)
            {
                attr |= System.Reflection.FieldAttributes.Public;
            }
            else
                attr |= System.Reflection.FieldAttributes.Private;
            this.fieldType = fieldType;
        }

        void InitializeCustomAttribute()
        {
            customAttributes = new Attribute[definition.CustomAttributes.Count];
            attributeTypes = new Type[customAttributes.Length];
            for (int i = 0; i < definition.CustomAttributes.Count; i++)
            {
                CustomAttribute attribute = definition.CustomAttributes[i];
                IType at = appdomain.GetType(attribute.AttributeType, null, null);
                try
                {
                    Attribute ins = attribute.CreateInstance(at, appdomain) as Attribute;

                    attributeTypes[i] = at.ReflectionType;
                    customAttributes[i] = ins;
                }
                catch
                {
                    attributeTypes[i] = typeof(Attribute);
                }
            }
        }

        public override System.Reflection.FieldAttributes Attributes
        {
            get
            {
                return attr;
            }
        }

        public override Type DeclaringType
        {
            get
            {
                return dType;
            }
        }

        public override RuntimeFieldHandle FieldHandle
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override Type FieldType
        {
            get
            {
                return fieldType.ReflectionType;
            }
        }

        public override string Name
        {
            get
            {
                return name;
            }
        }

        public override Type ReflectedType
        {
            get
            {
                return fieldType.ReflectionType;
            }
        }

        public override object[] GetCustomAttributes(bool inherit)
        {
            if (customAttributes == null)
                InitializeCustomAttribute();

            return customAttributes;
        }

        public override object[] GetCustomAttributes(Type attributeType, bool inherit)
        {
            if (customAttributes == null)
                InitializeCustomAttribute();

            List<object> res = new List<object>();
            for (int i = 0; i < customAttributes.Length; i++)
            {
                if (attributeTypes[i].Equals(attributeType))
                {
                    res.Add(customAttributes[i]);
                }
            }
            return res.ToArray();
        }

        public override object GetValue(object obj)
        {
            unsafe
            {
                ILTypeInstance ins;
                if (isStatic)
                {
                    ins = ilType.StaticInstance;
                }
                else
                {
                    if (obj is ILTypeInstance)
                        ins = (ILTypeInstance)obj;
                    else
                        ins = ((CrossBindingAdaptorType)obj).ILInstance;
                }
                return fieldType.TypeForCLR.CheckCLRTypes(ins[fieldIdx]);
            }
        }

        public override bool IsDefined(Type attributeType, bool inherit)
        {
            if (customAttributes == null)
                InitializeCustomAttribute();


            for (int i = 0; i < customAttributes.Length; i++)
            {
                if (attributeTypes[i].Equals(attributeType))
                {
                    return true;
                }
            }
            return false;
        }

        public override void SetValue(object obj, object value, BindingFlags invokeAttr, Binder binder, CultureInfo culture)
        {
            unsafe
            {
                if (value is CrossBindingAdaptorType)
                    value = ((CrossBindingAdaptorType)value).ILInstance;
                ILTypeInstance ins;
                if (isStatic)
                {
                    ins = ilType.StaticInstance;
                }
                else
                {
                    if (obj is ILTypeInstance)
                        ins = (ILTypeInstance)obj;
                    else
                        ins = ((CrossBindingAdaptorType)obj).ILInstance;
                }
                ins[fieldIdx] = value;
            }
        }
    }
}
