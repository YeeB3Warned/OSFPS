﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Assertions;

public static class NetworkSerializationUtils
{
    public static byte[] SerializeRpcCall(RpcInfo rpcInfo, object argumentsObj)
    {
        var argumentsType = argumentsObj.GetType();
        var argumentProperties = argumentsType.GetProperties();

        Assert.IsTrue(argumentProperties.Length == rpcInfo.ParameterTypes.Length);

        using (var memoryStream = new MemoryStream())
        {
            using (var binaryWriter = new BinaryWriter(memoryStream))
            {
                binaryWriter.Write(rpcInfo.Id);

                for (var i = 0; i < rpcInfo.ParameterTypes.Length; i++)
                {
                    var parameterName = rpcInfo.ParameterNames[i];
                    var parameterType = rpcInfo.ParameterTypes[i];

                    var argumentProperty = argumentProperties.First(argField =>
                        argField.Name == parameterName
                    );
                    var argumentType = argumentProperty.PropertyType;
                    var argument = argumentProperty.GetValue(argumentsObj);

                    Assert.IsTrue(
                        argumentType.IsEquivalentTo(parameterType),
                        $"RPC parameter {parameterName} has type {parameterType.AssemblyQualifiedName} but was passed {argumentType.AssemblyQualifiedName}."
                    );

                    SerializeObject(binaryWriter, argument, argumentType);
                }
            }

            return memoryStream.ToArray();
        }
    }
    public static object[] DeserializeRpcCallArguments(RpcInfo rpcInfo, BinaryReader reader)
    {
        return rpcInfo.ParameterTypes
            .Select(parameterType => Deserialize(reader, parameterType))
            .ToArray();
    }

    public static Type GetSmallestUIntTypeWithNumberOfBits(uint numberOfBits)
    {
        if (numberOfBits <= 8)
        {
            return typeof(byte);
        }
        else if (numberOfBits <= 16)
        {
            return typeof(ushort);
        }
        else if (numberOfBits <= 32)
        {
            return typeof(uint);
        }
        else if (numberOfBits <= 64)
        {
            return typeof(ulong);
        }
        else
        {
            throw new NotImplementedException();
        }
    }
    public static Type GetSmallestUIntTypeForMaxValue(ulong maxValue)
    {
        if (maxValue <= byte.MaxValue)
        {
            return typeof(byte);
        }
        else if (maxValue <= ushort.MaxValue)
        {
            return typeof(ushort);
        }
        else if (maxValue <= uint.MaxValue)
        {
            return typeof(uint);
        }
        else if (maxValue <= ulong.MaxValue)
        {
            return typeof(ulong);
        }
        else
        {
            throw new NotImplementedException();
        }
    }
    public static Type GetSmallestUIntTypeToHoldEnumValues(Type enumType)
    {
        Assert.IsTrue(enumType.IsEnum);

        var numEnumValues = enumType.GetEnumValues().Length;
        return GetSmallestUIntTypeForMaxValue((ulong)numEnumValues);
    }

    public static void Serialize<T>(BinaryWriter writer, T t, bool isNullableIfReferenceType = false)
    {
        SerializeObject(writer, t, typeof(T), isNullableIfReferenceType);
    }
    public static void SerializeObject(
        BinaryWriter writer, object obj, Type overrideType = null, bool isNullableIfReferenceType = false
    )
    {
        Assert.IsTrue(!isNullableIfReferenceType || (overrideType != null));

        var objType = overrideType ?? obj?.GetType();
        Assert.IsNotNull(objType);
        Assert.IsTrue((obj != null) || !objType.IsClass || isNullableIfReferenceType);

        var nullableUnderlyingType = Nullable.GetUnderlyingType(objType);
        if ((nullableUnderlyingType == null) && isNullableIfReferenceType && objType.IsClass)
        {
            nullableUnderlyingType = objType;
        }

        if (nullableUnderlyingType != null)
        {
            writer.Write(obj != null);

            if (obj != null)
            {
                objType = nullableUnderlyingType;
            }
            else
            {
                return;
            }
        }

        if (objType == typeof(bool))
        {
            writer.Write((bool)obj);
        }
        else if (objType == typeof(sbyte))
        {
            writer.Write((sbyte)obj);
        }
        else if (objType == typeof(byte))
        {
            writer.Write((byte)obj);
        }
        else if (objType == typeof(ushort))
        {
            writer.Write((ushort)obj);
        }
        else if (objType == typeof(short))
        {
            writer.Write((short)obj);
        }
        else if (objType == typeof(uint))
        {
            writer.Write((uint)obj);
        }
        else if (objType == typeof(int))
        {
            writer.Write((int)obj);
        }
        else if (objType == typeof(ulong))
        {
            writer.Write((ulong)obj);
        }
        else if (objType == typeof(long))
        {
            writer.Write((long)obj);
        }
        else if (objType == typeof(float))
        {
            writer.Write((float)obj);
        }
        else if (objType == typeof(double))
        {
            writer.Write((double)obj);
        }
        else if (objType == typeof(decimal))
        {
            writer.Write((decimal)obj);
        }
        else if (objType == typeof(char))
        {
            writer.Write((char)obj);
        }
        else if (objType == typeof(string))
        {
            writer.Write((string)obj);
        }
        else if (objType == typeof(Vector2))
        {
            Serialize(writer, (Vector2)obj);
        }
        else if (objType == typeof(Vector3))
        {
            Serialize(writer, (Vector3)obj);
        }
        else if (typeof(INetworkSerializable).IsAssignableFrom(obj.GetType()))
        {
            ((INetworkSerializable)obj).Serialize(writer);
        }
        else if (typeof(ICollection).IsAssignableFrom(objType))
        {
            var collection = (ICollection)obj;
            writer.Write((uint)collection.Count);

            foreach (var element in collection)
            {
                SerializeObject(writer, element, overrideType: null, isNullableIfReferenceType: false);
            }
        }
        else
        {
            if (objType.IsEnum)
            {
                var smallestTypeToHoldEnumValues = GetSmallestUIntTypeToHoldEnumValues(objType);
                SerializeObject(writer, Convert.ChangeType(obj, smallestTypeToHoldEnumValues));
            }
            else if (objType.IsClass || objType.IsValueType)
            {
                var objFields = objType.GetFields();
                foreach (var objField in objFields)
                {
                    SerializeObject(writer, objField.GetValue(obj), objField.FieldType);
                }

                var objProperties = objType.GetProperties();
                foreach (var objProperty in objProperties)
                {
                    SerializeObject(writer, objProperty.GetValue(obj), objProperty.PropertyType);
                }
            }
            else
            {
                throw new NotImplementedException($"Cannot serialize type: {objType.AssemblyQualifiedName}");
            }
        }
    }


    public static uint GetChangeMask(Type type, object oldValue, object newValue)
    {
        var typeInfo = GetTypeToNetworkSynchronizeInfo(type);
        uint changeMask = 0;
        uint changeMaskBitIndex = 0;

        foreach (var field in typeInfo.FieldsToSynchronize)
        {
            var oldFieldValue = field.GetValue(oldValue);
            var newFieldValue = field.GetValue(newValue);

            BitUtilities.SetBit(ref changeMask, (byte)changeMaskBitIndex, !object.Equals(newFieldValue, oldFieldValue));
            changeMaskBitIndex++;
        }

        foreach (var property in typeInfo.PropertiesToSynchronize)
        {
            var oldPropertyValue = property.GetValue(oldValue);
            var newPropertyValue = property.GetValue(newValue);

            BitUtilities.SetBit(ref changeMask, (byte)changeMaskBitIndex, !object.Equals(newPropertyValue, oldPropertyValue));
            changeMaskBitIndex++;
        }

        return changeMask;
    }

    public struct TypeToNetworkSynchronizeInfo
    {
        public FieldInfo[] FieldsToSynchronize;
        public PropertyInfo[] PropertiesToSynchronize;
    }
    private static TypeToNetworkSynchronizeInfo GetTypeToNetworkSynchronizeInfo(Type type)
    {
        return new TypeToNetworkSynchronizeInfo
        {
            FieldsToSynchronize = type.GetFields()
                .Where(field => !Attribute.IsDefined(field, typeof(NotNetworkSynchronizedAttribute)))
                .ToArray(),
            PropertiesToSynchronize = type.GetProperties()
                .Where(property =>
                    property.CanRead &&
                    property.CanWrite &&
                    !Attribute.IsDefined(property, typeof(NotNetworkSynchronizedAttribute))
                )
                .ToArray()
        };
    }

    public static void SerializeGivenChangeMask(BinaryWriter writer, Type type, object value, uint changeMask)
    {
        var typeInfo = GetTypeToNetworkSynchronizeInfo(type);
        uint changeMaskBitIndex = 0;

        foreach (var field in typeInfo.FieldsToSynchronize)
        {
            if (BitUtilities.GetBit(changeMask, (byte)changeMaskBitIndex))
            {
                var isNullableIfReferenceType = field.FieldType.IsClass && !Attribute.IsDefined(field, typeof(NonNullableAttribute));
                SerializeObject(
                    writer, field.GetValue(value), field.FieldType, isNullableIfReferenceType
                );
            }
            changeMaskBitIndex++;
        }

        foreach (var property in typeInfo.PropertiesToSynchronize)
        {
            if (BitUtilities.GetBit(changeMask, (byte)changeMaskBitIndex))
            {
                var isNullableIfReferenceType = property.PropertyType.IsClass && !Attribute.IsDefined(property, typeof(NonNullableAttribute));
                SerializeObject(
                    writer, property.GetValue(value), property.PropertyType, isNullableIfReferenceType
                );
            }
            changeMaskBitIndex++;
        }
    }
    public static void SerializeDelta(BinaryWriter writer, Type type, object oldValue, object newValue)
    {
        var changeMask = GetChangeMask(type, oldValue, newValue);
        writer.Write(changeMask);
        SerializeGivenChangeMask(writer, type, newValue, changeMask);
    }

    public static void DeserializeGivenChangeMask(BinaryReader reader, Type type, object oldValue, uint changeMask)
    {
        var typeInfo = GetTypeToNetworkSynchronizeInfo(type);
        byte changeMaskBitIndex = 0;

        foreach (var field in typeInfo.FieldsToSynchronize)
        {
            if (BitUtilities.GetBit(changeMask, changeMaskBitIndex))
            {
                var isNullableIfReferenceType = field.FieldType.IsClass && !Attribute.IsDefined(field, typeof(NonNullableAttribute));
                var newFieldValue = NetworkSerializationUtils.Deserialize(
                    reader, field.FieldType, isNullableIfReferenceType
                );
                field.SetValue(oldValue, newFieldValue);
            }
            changeMaskBitIndex++;
        }

        foreach (var property in typeInfo.PropertiesToSynchronize)
        {
            if (BitUtilities.GetBit(changeMask, changeMaskBitIndex))
            {
                var isNullableIfReferenceType = property.PropertyType.IsClass && !Attribute.IsDefined(property, typeof(NonNullableAttribute));
                var newPropertyValue = NetworkSerializationUtils.Deserialize(
                    reader, property.PropertyType, isNullableIfReferenceType
                );
                property.SetValue(oldValue, newPropertyValue);
            }
            changeMaskBitIndex++;
        }
    }
    public static void DeserializeDelta(BinaryReader reader, Type type, object oldValue)
    {
        var changeMask = reader.ReadUInt32();
        DeserializeGivenChangeMask(reader, type, oldValue, changeMask);
    }

    public static object Deserialize(BinaryReader reader, Type type, bool isNullableIfReferenceType = false)
    {
        var nullableUnderlyingType = Nullable.GetUnderlyingType(type);
        if ((nullableUnderlyingType == null) && isNullableIfReferenceType && type.IsClass)
        {
            nullableUnderlyingType = type;
        }

        if (nullableUnderlyingType != null)
        {
            var objHasValue = reader.ReadBoolean();
            return objHasValue ? Deserialize(reader, nullableUnderlyingType) : null;
        }
        else if (type == typeof(bool))
        {
            return reader.ReadBoolean();
        }
        else if (type == typeof(sbyte))
        {
            return reader.ReadSByte();
        }
        else if (type == typeof(byte))
        {
            return reader.ReadByte();
        }
        else if (type == typeof(ushort))
        {
            return reader.ReadUInt16();
        }
        else if (type == typeof(short))
        {
            return reader.ReadInt16();
        }
        else if (type == typeof(uint))
        {
            return reader.ReadUInt32();
        }
        else if (type == typeof(int))
        {
            return reader.ReadInt32();
        }
        else if (type == typeof(ulong))
        {
            return reader.ReadUInt64();
        }
        else if (type == typeof(long))
        {
            return reader.ReadInt64();
        }
        else if (type == typeof(float))
        {
            return reader.ReadSingle();
        }
        else if (type == typeof(double))
        {
            return reader.ReadDouble();
        }
        else if (type == typeof(decimal))
        {
            return reader.ReadDecimal();
        }
        else if (type == typeof(char))
        {
            return reader.ReadChar();
        }
        else if (type == typeof(string))
        {
            return reader.ReadString();
        }
        else if (type == typeof(Vector2))
        {
            var result = new Vector2();
            Deserialize(reader, ref result);

            return result;
        }
        else if (type == typeof(Vector3))
        {
            var result = new Vector3();
            Deserialize(reader, ref result);

            return result;
        }
        else if (typeof(INetworkSerializable).IsAssignableFrom(type))
        {
            var result = Activator.CreateInstance(type);
            ((INetworkSerializable)result).Deserialize(reader);

            return result;
        }
        else if (typeof(IEnumerable).IsAssignableFrom(type))
        {
            if (typeof(Array).IsAssignableFrom(type))
            {
                var elementType = type.GetElementType();
                var elementCount = reader.ReadUInt32();
                var array = Array.CreateInstance(elementType, elementCount);

                for (var i = 0; i < elementCount; i++)
                {
                    var element = Deserialize(reader, elementType, isNullableIfReferenceType: false);
                    array.SetValue(element, i);
                }

                return array;
            }
            else if (typeof(IList).IsAssignableFrom(type))
            {
                var list = (IList)Activator.CreateInstance(type);
                var elementType = type.GenericTypeArguments[0];
                var elementCount = reader.ReadUInt32();

                for (var i = 0; i < elementCount; i++)
                {
                    var element = Deserialize(reader, elementType, isNullableIfReferenceType: false);
                    list.Add(element);
                }

                return list;
            }
            else
            {
                throw new NotImplementedException();
            }
        }
        else
        {
            if (type.IsEnum)
            {
                var smallestTypeToHoldEnumValues = GetSmallestUIntTypeToHoldEnumValues(type);
                var enumValueAsInt = Deserialize(reader, smallestTypeToHoldEnumValues);

                return Enum.ToObject(type, enumValueAsInt);
            }
            else if (type.IsClass || type.IsValueType)
            {
                var result = Activator.CreateInstance(type);

                var objFields = type.GetFields();
                foreach (var field in objFields)
                {
                    field.SetValue(result, Deserialize(reader, field.FieldType));
                }

                var objProperties = type.GetProperties();
                foreach (var property in objProperties)
                {
                    property.SetValue(result, Deserialize(reader, property.PropertyType));
                }

                return result;
            }
            else
            {
                throw new NotImplementedException($"Cannot deserialize type: {type.AssemblyQualifiedName}");
            }
        }
    }
    public static T Deserialize<T>(BinaryReader reader, bool isNullableIfReferenceType = false)
    {
        return (T)Deserialize(reader, typeof(T), isNullableIfReferenceType);
    }

    public static void Serialize(BinaryWriter writer, Vector2 v)
    {
        writer.Write(v.x);
        writer.Write(v.y);
    }
    public static void Deserialize(BinaryReader reader, ref Vector2 v)
    {
        v.x = reader.ReadSingle();
        v.y = reader.ReadSingle();
    }
    public static void Serialize(BinaryWriter writer, Vector3 v)
    {
        writer.Write(v.x);
        writer.Write(v.y);
        writer.Write(v.z);
    }
    public static void Deserialize(BinaryReader reader, ref Vector3 v)
    {
        v.x = reader.ReadSingle();
        v.y = reader.ReadSingle();
        v.z = reader.ReadSingle();
    }

    public static void Serialize<T>(BinaryWriter writer, List<T> list) where T : INetworkSerializable
    {
        writer.Write(list.Count);

        foreach(var element in list)
        {
            element.Serialize(writer);
        }
    }
    public static void Deserialize<T>(BinaryReader reader, List<T> list) where T : INetworkSerializable, new()
    {
        list.Clear();

        var listSize = reader.ReadInt32();

        for(var i = 0; i < listSize; i++)
        {
            var element = new T();
            element.Deserialize(reader);

            list.Add(element);
        }
    }

    public static void Serialize<T>(BinaryWriter writer, List<T> list, Action<BinaryWriter, T> serializeElementFunc)
    {
        writer.Write(list.Count);

        foreach (var element in list)
        {
            serializeElementFunc(writer, element);
        }
    }
    public static void Deserialize<T>(BinaryReader reader, List<T> list, Func<BinaryReader, T> deserializeElementFunc)
    {
        list.Clear();

        var listSize = reader.ReadInt32();

        for (var i = 0; i < listSize; i++)
        {
            var element = deserializeElementFunc(reader);
            list.Add(element);
        }
    }

    public static void SerializeNullable<T>(BinaryWriter writer, T value) where T : INetworkSerializable
    {
        writer.Write(value != null);

        if (value != null)
        {
            value.Serialize(writer);
        }
    }
    public static T DeserializeNullable<T>(BinaryReader reader) where T : INetworkSerializable, new()
    {
        var isNotNull = reader.ReadBoolean();

        if (isNotNull)
        {
            var value = new T();
            value.Deserialize(reader);

            return value;
        }
        else
        {
            return default(T);
        }
    }
}