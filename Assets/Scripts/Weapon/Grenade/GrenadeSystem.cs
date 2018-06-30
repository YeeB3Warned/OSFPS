﻿using UnityEngine;
using Unity.Entities;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

public class GrenadeSystem : ComponentSystem
{
    public struct Data
    {
        public int Length;
        public ComponentArray<GrenadeComponent> GrenadeComponent;
    }

    public static GrenadeSystem Instance;

    public GrenadeSystem()
    {
        Instance = this;
    }

    public void ServerPlayerThrowGrenade(Server server, PlayerObjectComponent playerObjectComponent)
    {
        var playerObjectState = playerObjectComponent.State;
        if (!playerObjectState.CanThrowGrenade) return;

        var throwRay = PlayerObjectSystem.Instance.GetShotRay(playerObjectComponent);
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

        // Make grenade ignore collisions with thrower.
        GameObjectExtensions.IgnoreCollisionsRecursive(grenadeObject, playerObjectComponent.gameObject);

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
            ServerGrenadeOnCollisionEnter(OsFps.Instance.Server, grenadeComponent, collision);
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

    protected override void OnUpdate()
    {
        var server = OsFps.Instance?.Server;
        if (server != null)
        {
            ServerOnUpdate(server);
        }
    }

    [Inject] private Data data;

    private void ServerOnUpdate(Server server)
    {
        var deltaTime = Time.deltaTime;

        for (var i = 0; i < data.Length; i++)
        {
            var grenade = data.GrenadeComponent[i].State;

            if (grenade.TimeUntilDetonation > 0)
            {
                grenade.TimeUntilDetonation -= deltaTime;
            }
        }

        var grenadesToDetonate = new List<GrenadeComponent>();
        for (var i = 0; i < data.Length; i++)
        {
            var grenade = data.GrenadeComponent[i].State;

            if (grenade.TimeUntilDetonation <= 0)
            {
                grenadesToDetonate.Add(data.GrenadeComponent[i]);
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
        var grenadePosition = (float3)grenadeComponent.transform.position;

        // apply damage & forces to players within range
        OsFps.Instance.ApplyExplosionDamageAndForces(
            server, grenadePosition, grenadeDefinition.ExplosionRadius, OsFps.GrenadeExplosionForce,
            grenadeDefinition.Damage, grenade.ThrowerPlayerId
        );

        // destroy grenade object
        Object.Destroy(grenadeComponent.gameObject);

        // send message
        server.ServerPeer.CallRpcOnAllClients("ClientOnDetonateGrenade", server.reliableChannelId, new
        {
            id = grenade.Id,
            position = grenadePosition,
            type = grenade.Type
        });
    }
}