﻿using System.IO;
using UnityEngine;

[System.Serializable]
public class RigidBodyState : INetworkSerializable
{
    public Vector3 Position;
    public Vector3 EulerAngles;
    public Vector3 Velocity;
    public Vector3 AngularVelocity;
    
    public virtual void Serialize(BinaryWriter writer)
    {
        NetworkSerializationUtils.Serialize(writer, Position);
        NetworkSerializationUtils.Serialize(writer, EulerAngles);
        NetworkSerializationUtils.Serialize(writer, Velocity);
        NetworkSerializationUtils.Serialize(writer, AngularVelocity);
    }
    public virtual void Deserialize(BinaryReader reader)
    {
        NetworkSerializationUtils.Deserialize(reader, ref Position);
        NetworkSerializationUtils.Deserialize(reader, ref EulerAngles);
        NetworkSerializationUtils.Deserialize(reader, ref Velocity);
        NetworkSerializationUtils.Deserialize(reader, ref AngularVelocity);
    }
}