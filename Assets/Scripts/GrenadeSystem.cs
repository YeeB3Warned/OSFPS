﻿using System.Linq;
using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;

public class GrenadeSystem : ComponentSystem
{
    public struct Group
    {
        public GrenadeComponent GrenadeComponent;
    }

    public static GrenadeSystem Instance;

    public GrenadeSystem()
    {
        Instance = this;
    }

    protected override void OnUpdate()
    {
        var server = OsFps.Instance.Server;
        if (server != null)
        {
            ServerOnUpdate(server);
        }
    }
    
    private void ServerOnUpdate(Server server)
    {
        var deltaTime = Time.deltaTime;

        var entities = GetEntities<Group>();
        foreach (var entity in entities)
        {
            var grenade = server.CurrentGameState.Grenades
                .FirstOrDefault(g => g.Id == entity.GrenadeComponent.Id);

            if (grenade.IsFuseBurning)
            {
                if (grenade.TimeUntilDetonation > 0)
                {
                    grenade.TimeUntilDetonation -= deltaTime;
                }
            }
        }

        var grenadesToDetonate = new List<GrenadeState>();
        foreach (var entity in entities)
        {
            var grenade = server.CurrentGameState.Grenades
                .FirstOrDefault(g => g.Id == entity.GrenadeComponent.Id);

            if (grenade.IsFuseBurning && (grenade.TimeUntilDetonation <= 0))
            {
                grenadesToDetonate.Add(grenade);
            }
        }

        foreach (var grenadeToDetonate in grenadesToDetonate)
        {
            ServerDetonateGrenade(server, grenadeToDetonate);
        }
    }
    private void ServerDetonateGrenade(Server server, GrenadeState grenade)
    {
        var grenadeComponent = OsFps.Instance.FindGrenade(grenade.Id);
        var grenadeDefinition = OsFps.GetGrenadeDefinitionByType(grenade.Type);
        var grenadePosition = grenadeComponent.transform.position;

        // apply damage & forces to players within range
        var affectedColliders = Physics.OverlapSphere(
            grenadePosition, grenadeDefinition.ExplosionRadius
        );

        foreach (var collider in affectedColliders)
        {
            // Apply damage.
            var playerComponent = collider.gameObject.GetComponent<PlayerComponent>();
            if (playerComponent != null)
            {
                var playerState = server.CurrentGameState.Players.FirstOrDefault(ps => ps.Id == playerComponent.Id);
                var closestPointToGrenade = collider.ClosestPoint(grenadePosition);
                var distanceFromGrenade = Vector3.Distance(closestPointToGrenade, grenadePosition);
                var unclampedDamagePercent = (grenadeDefinition.ExplosionRadius - distanceFromGrenade) / grenadeDefinition.ExplosionRadius;
                var damagePercent = Mathf.Max(unclampedDamagePercent, 0);
                var damage = damagePercent * grenadeDefinition.Damage;

                // TODO: don't call system directly
                PlayerSystem.Instance.ServerDamagePlayer(server, playerState, (int)damage, null);
            }

            // Apply forces.
            var rigidbody = collider.gameObject.GetComponent<Rigidbody>();
            if (rigidbody != null)
            {
                rigidbody.AddExplosionForce(
                    OsFps.GrenadeExplosionForce, grenadePosition, grenadeDefinition.ExplosionRadius
                );
            }
        }

        // destroy grenade object
        Object.Destroy(grenadeComponent.gameObject);

        // remove grenade state
        server.CurrentGameState.Grenades.RemoveAll(gs => gs.Id == grenade.Id);

        // send message
        var message = new DetonateGrenadeMessage
        {
            Id = grenade.Id,
            Position = grenadePosition,
            Type = grenade.Type
        };
        server.SendMessageToAllClients(server.reliableChannelId, message);
    }

    public void ServerPlayerThrowGrenade(Server server, PlayerState playerState)
    {
        if (!playerState.CanThrowGrenade) return;

        var playerComponent = OsFps.Instance.FindPlayerComponent(playerState.Id);
        if (playerComponent == null) return;

        var throwRay = new Ray(
            playerComponent.CameraPointObject.transform.position,
            playerComponent.CameraPointObject.transform.forward
        );
        var grenadeState = new GrenadeState
        {
            Id = server.GenerateNetworkId(),
            Type = playerState.CurrentGrenadeType,
            IsFuseBurning = false,
            TimeUntilDetonation = OsFps.GetGrenadeDefinitionByType(playerState.CurrentGrenadeType).TimeAfterHitUntilDetonation,
            RigidBodyState = new RigidBodyState
            {
                Position = throwRay.origin,
                EulerAngles = Quaternion.LookRotation(throwRay.direction, Vector3.up).eulerAngles,
                Velocity = OsFps.GrenadeThrowSpeed * throwRay.direction,
                AngularVelocity = Vector3.zero
            }
        };
        var grenadeObject = OsFps.Instance.SpawnLocalGrenadeObject(grenadeState);

        var grenadeComponent = grenadeObject.GetComponent<GrenadeComponent>();
        server.CurrentGameState.Grenades.Add(grenadeState);

        playerState.GrenadesLeftByType[playerState.CurrentGrenadeType]--;
        playerState.TimeUntilCanThrowGrenade = OsFps.GrenadeThrowInterval;
    }
    public void ServerGrenadeOnCollisionEnter(Server server, GrenadeComponent grenadeComponent, Collision collision)
    {
        Debug.Log("Grenade collided with " + collision.gameObject.name);

        var grenadeState = server.CurrentGameState.Grenades.First(g => g.Id == grenadeComponent.Id);
        grenadeState.IsFuseBurning = true;
    }
}