﻿using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;
using System.Linq;

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
        var server = OsFps.Instance?.Server;
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
            var grenade = entity.GrenadeComponent.State;

            if (grenade.TimeUntilDetonation > 0)
            {
                grenade.TimeUntilDetonation -= deltaTime;
            }
        }

        var grenadesToDetonate = new List<GrenadeComponent>();
        foreach (var entity in entities)
        {
            var grenade = entity.GrenadeComponent.State;

            if (grenade.TimeUntilDetonation <= 0)
            {
                grenadesToDetonate.Add(entity.GrenadeComponent);
            }
        }

        foreach (var grenadeToDetonate in grenadesToDetonate)
        {
            ServerDetonateGrenade(server, grenadeToDetonate);
        }
    }

    private void ServerDetonateGrenade(Server server, GrenadeComponent grenadeComponent)
    {
        var grenade = grenadeComponent.State;
        var grenadeDefinition = GetGrenadeDefinitionByType(grenade.Type);
        var grenadePosition = grenadeComponent.transform.position;

        // apply damage & forces to players within range
        OsFps.Instance.ApplyExplosionDamageAndForces(
            server, grenadePosition, grenadeDefinition.ExplosionRadius, OsFps.GrenadeExplosionForce,
            grenadeDefinition.Damage, grenade.ThrowerPlayerId
        );

        // destroy grenade object
        Object.Destroy(grenadeComponent.gameObject);

        // send message
        OsFps.Instance.CallRpcOnAllClients("ClientOnDetonateGrenade", server.reliableChannelId, new
        {
            id = grenade.Id,
            position = grenadePosition,
            type = grenade.Type
        });
    }

    public void ServerPlayerThrowGrenade(Server server, PlayerObjectComponent playerObjectComponent)
    {
        var playerObjectState = playerObjectComponent.State;
        if (!playerObjectState.CanThrowGrenade) return;

        var throwRay = PlayerSystem.Instance.GetShotRay(playerObjectComponent);
        throwRay.origin += (0.5f * throwRay.direction);
        var currentGrenadeSlot = playerObjectState.GrenadeSlots[playerObjectState.CurrentGrenadeSlotIndex];

        var grenadeState = new GrenadeState
        {
            Id = server.GenerateNetworkId(),
            Type = currentGrenadeSlot.GrenadeType,
            IsActive = true,
            TimeUntilDetonation = null,
            RigidBodyState = new RigidBodyState
            {
                Position = throwRay.origin,
                EulerAngles = Quaternion.LookRotation(throwRay.direction, Vector3.up).eulerAngles,
                Velocity = OsFps.GrenadeThrowSpeed * throwRay.direction,
                AngularVelocity = Vector3.zero
            },
            ThrowerPlayerId = playerObjectState.Id
        };
        var grenadeObject = GrenadeSpawnerSystem.Instance.SpawnLocalGrenadeObject(grenadeState);

        currentGrenadeSlot.GrenadeCount--;
        playerObjectState.TimeUntilCanThrowGrenade = OsFps.GrenadeThrowInterval;
    }
    public void StickStickyGrenadeToObject(GrenadeComponent grenadeComponent, GameObject hitObject)
    {
        grenadeComponent.Rigidbody.isKinematic = true;
        grenadeComponent.Collider.isTrigger = true;
        grenadeComponent.transform.SetParent(hitObject.transform);
    }
    public void ServerGrenadeOnCollisionEnter(Server server, GrenadeComponent grenadeComponent, Collision collision)
    {
        var grenadeState = grenadeComponent.State;

        if (grenadeState.IsActive)
        {
            grenadeState.TimeUntilDetonation = GetGrenadeDefinitionByType(grenadeState.Type).TimeAfterHitUntilDetonation;

            if (grenadeComponent.State.Type == GrenadeType.Fragmentation)
            {
                var audioSource = grenadeComponent.GetComponent<AudioSource>();
                audioSource.PlayOneShot(OsFps.Instance.FragGrenadeBounceSound);
            }
            else if (grenadeComponent.State.Type == GrenadeType.Sticky)
            {
                StickStickyGrenadeToObject(grenadeComponent, collision.gameObject);
            }
        }
    }

    public void GrenadeOnCollisionEnter(GrenadeComponent grenadeComponent, Collision collision)
    {
        if (OsFps.Instance.Server != null)
        {
            GrenadeSystem.Instance.ServerGrenadeOnCollisionEnter(OsFps.Instance.Server, grenadeComponent, collision);
        }
    }

    public GrenadeComponent FindGrenadeComponent(uint id)
    {
        return Object.FindObjectsOfType<GrenadeComponent>()
           .FirstOrDefault(g => g.State.Id == id);
    }
    public GrenadeDefinition GetGrenadeDefinitionByType(GrenadeType type)
    {
        return OsFps.Instance.GrenadeDefinitionComponents
            .FirstOrDefault(gdc => gdc.Definition.Type == type)
            ?.Definition;
    }
}