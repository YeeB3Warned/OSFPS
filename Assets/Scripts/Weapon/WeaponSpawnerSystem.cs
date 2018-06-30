﻿using UnityEngine;
using Unity.Entities;
using System.Linq;
using System.Collections.Generic;

public class WeaponSpawnerSystem : ComponentSystem
{
    public struct Data
    {
        public int Length;
        public ComponentArray<WeaponSpawnerComponent> WeaponSpawnerComponent;
    }

    public static WeaponSpawnerSystem Instance;

    public WeaponSpawnerSystem()
    {
        Instance = this;
    }

    public void ServerSpawnWeapon(Server server, WeaponSpawnerState weaponSpawnerState)
    {
        if (weaponSpawnerState.TimeUntilNextSpawn > 0) return;

        var weaponDefinition = WeaponObjectSystem.Instance.GetWeaponDefinitionByType(weaponSpawnerState.Type);
        var bulletsLeft = weaponDefinition.MaxAmmo / 2;
        var bulletsLeftInMagazine = Mathf.Min(weaponDefinition.BulletsPerMagazine, bulletsLeft);
        var weaponSpawnerComponent = FindWeaponSpawnerComponent(weaponSpawnerState.Id);

        var weaponObjectState = new WeaponObjectState
        {
            Id = server.GenerateNetworkId(),
            Type = weaponSpawnerState.Type,
            BulletsLeftInMagazine = (ushort)bulletsLeftInMagazine,
            BulletsLeftOutOfMagazine = (ushort)(bulletsLeft - bulletsLeftInMagazine),
            RigidBodyState = new RigidBodyState
            {
                Position = weaponSpawnerComponent.transform.position,
                EulerAngles = weaponSpawnerComponent.transform.eulerAngles,
                Velocity = Vector3.zero,
                AngularVelocity = Vector3.zero
            },
            WeaponSpawnerId = weaponSpawnerState.Id
        };
        SpawnLocalWeaponObject(weaponObjectState);
    }
    public GameObject SpawnLocalWeaponObject(WeaponObjectState weaponObjectState)
    {
        var weaponPrefab = WeaponObjectSystem.Instance.GetWeaponDefinitionByType(weaponObjectState.Type).Prefab;
        var weaponObject = GameObject.Instantiate(
            weaponPrefab,
            weaponObjectState.RigidBodyState.Position,
            Quaternion.Euler(weaponObjectState.RigidBodyState.EulerAngles)
        );

        var weaponObjectComponent = weaponObject.GetComponent<WeaponComponent>();
        weaponObjectComponent.State = weaponObjectState;

        var rigidbody = weaponObjectComponent.Rigidbody;
        rigidbody.velocity = weaponObjectState.RigidBodyState.Velocity;
        rigidbody.angularVelocity = weaponObjectState.RigidBodyState.AngularVelocity;

        return weaponObject;
    }

    public WeaponSpawnerComponent FindWeaponSpawnerComponent(uint id)
    {
        return Object.FindObjectsOfType<WeaponSpawnerComponent>()
            .FirstOrDefault(wsc => wsc.State.Id == id);
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
        for (var i = 0; i < data.Length; i++)
        {
            var weaponSpawner = data.WeaponSpawnerComponent[i].State;

            // spawn interval
            if (weaponSpawner.TimeUntilNextSpawn > 0)
            {
                weaponSpawner.TimeUntilNextSpawn -= Time.deltaTime;
            }

            if (weaponSpawner.TimeUntilNextSpawn <= 0)
            {
                ServerSpawnWeapon(server, weaponSpawner);
                weaponSpawner.TimeUntilNextSpawn = null;
            }
        }
    }
}