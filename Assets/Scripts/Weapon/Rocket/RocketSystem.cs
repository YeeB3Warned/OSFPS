﻿using UnityEngine;
using Unity.Entities;
using System.Linq;
using Unity.Mathematics;
using System.Collections.Generic;

public class RocketSystem : ComponentSystem
{
    public struct Data
    {
        public RocketComponent RocketComponent;
    }

    public static RocketSystem Instance;

    public RocketSystem()
    {
        Instance = this;
    }

    protected override void OnUpdate()
    {
    }

    public RocketComponent FindRocketComponent(uint id)
    {
        return Object.FindObjectsOfType<RocketComponent>()
           .FirstOrDefault(g => g.State.Id == id);
    }

    public GameObject SpawnLocalRocketObject(RocketState rocketState)
    {
        var rocketObject = GameObject.Instantiate(
            OsFps.Instance.RocketPrefab,
            rocketState.RigidBodyState.Position,
            Quaternion.Euler(rocketState.RigidBodyState.EulerAngles)
        );

        var rocketComponent = rocketObject.GetComponent<RocketComponent>();
        rocketComponent.State = rocketState;

        var rigidbody = rocketComponent.Rigidbody;
        rigidbody.velocity = rocketState.RigidBodyState.Velocity;
        rigidbody.angularVelocity = rocketState.RigidBodyState.AngularVelocity;

        return rocketObject;
    }
    public void RocketOnCollisionEnter(RocketComponent rocketComponent, Collision collision)
    {
        if (OsFps.Instance.Server != null)
        {
            RocketSystem.Instance.ServerRocketOnCollisionEnter(OsFps.Instance.Server, rocketComponent, collision);
        }
    }

    public void ServerRocketOnCollisionEnter(Server server, RocketComponent rocketComponent, Collision collision)
    {
        ServerDetonateRocket(server, rocketComponent);
    }

    private void ServerDetonateRocket(Server server, RocketComponent rocketComponent)
    {
        var rocketPosition = (float3)rocketComponent.transform.position;

        // apply damage & forces to players within range
        var rocketLauncherDefinition = WeaponSystem.Instance.GetWeaponDefinitionByType(WeaponType.RocketLauncher);
        WeaponSystem.Instance.ApplyExplosionDamageAndForces(
            server, rocketPosition, OsFps.RocketExplosionRadius, OsFps.RocketExplosionForce,
            rocketLauncherDefinition.DamagePerBullet, rocketComponent.State.ShooterPlayerId
        );

        // destroy rocket object
        Object.Destroy(rocketComponent.gameObject);

        // send message
        server.ServerPeer.CallRpcOnAllClients("ClientOnDetonateRocket", server.ServerPeer.reliableChannelId, new
        {
            id = rocketComponent.State.Id,
            position = rocketPosition
        });
    }

    public void ShowRocketExplosion(Vector3 position)
    {
        var explosionPrefab = OsFps.Instance.RocketExplosionPrefab;
        GameObject explosionObject = Object.Instantiate(
            explosionPrefab, position, Quaternion.identity
        );

        var audioSource = explosionObject.GetComponent<AudioSource>();
        audioSource?.Play();

        Object.Destroy(explosionObject, OsFps.RocketExplosionDuration);
    }
}