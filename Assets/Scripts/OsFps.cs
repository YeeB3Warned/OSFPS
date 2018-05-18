﻿using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

/*
TODO
====
-Handle networked shot intervals better.
-Improve server-side verification (use round trip time).
-Send shot rays from client.
-Set is fire pressed to false when switching weapons.
-Fix other players reloading.
-Improve player movement.
-Add player head & body.
-Add shields.
-Attribute grenade kills to the correct player.
-Improve grenade trajectories.
-Don't add bullets to magazine when running over weapon.
-Implement delta game state sending.
-Make weapons spawn repeatedly.
-Remove system instances.
*/

public class OsFps : MonoBehaviour
{
    public const string LocalHostIpv4Address = "127.0.0.1";

    public const string PlayerTag = "Player";
    public const string SpawnPointTag = "Respawn";

    public const int MaxPlayerHealth = 100;
    public const float RespawnTime = 3;

    public const float MuzzleFlashDuration = 0.1f;
    public const int MaxWeaponCount = 2;

    public const int MaxGrenadesPerType = 2;
    public const float GrenadeThrowInterval = 1;
    public const float GrenadeThrowSpeed = 10;
    public const float GrenadeExplosionForce = 500;
    public const float GrenadeExplosionDuration = 0.5f;

    public const int FireMouseButtonNumber = 0;
    public const KeyCode MoveForwardKeyCode = KeyCode.W;
    public const KeyCode MoveBackwardKeyCode = KeyCode.S;
    public const KeyCode MoveRightKeyCode = KeyCode.D;
    public const KeyCode MoveLeftKeyCode = KeyCode.A;
    public const KeyCode ReloadKeyCode = KeyCode.R;
    public const KeyCode ThrowGrenadeKeyCode = KeyCode.G;
    public const KeyCode ShowScoreboardKeyCode = KeyCode.Tab;
    public const KeyCode ChatKeyCode = KeyCode.Return;
    public const KeyCode ToggleMenuKeyCode = KeyCode.Escape;

    public const float KillPlaneY = -100;

    public static WeaponDefinition PistolDefinition = new WeaponDefinition
    {
        Type = WeaponType.Pistol,
        MaxAmmo = 100,
        BulletsPerMagazine = 10,
        DamagePerBullet = 10,
        ReloadTime = 1,
        ShotInterval = 0.4f,
        IsAutomatic = false,
        SpawnInterval = 10
    };
    public static WeaponDefinition SmgDefinition = new WeaponDefinition
    {
        Type = WeaponType.Smg,
        MaxAmmo = 100,
        BulletsPerMagazine = 10,
        DamagePerBullet = 10,
        ReloadTime = 1,
        ShotInterval = 0.1f,
        IsAutomatic = true,
        SpawnInterval = 20
    };
    public static WeaponDefinition GetWeaponDefinitionByType(WeaponType type)
    {
        switch (type)
        {
            case WeaponType.Pistol:
                return PistolDefinition;
            case WeaponType.Smg:
                return SmgDefinition;
            default:
                throw new System.NotImplementedException();
        }
    }

    public static GrenadeDefinition FragmentationGrenadeDefinition = new GrenadeDefinition
    {
        Type = GrenadeType.Fragmentation,
        Damage = 90,
        TimeAfterHitUntilDetonation = 1,
        ExplosionRadius = 4,
        SpawnInterval = 20
    };
    public static GrenadeDefinition StickyGrenadeDefinition = new GrenadeDefinition
    {
        Type = GrenadeType.Sticky,
        Damage = 90,
        TimeAfterHitUntilDetonation = 1,
        ExplosionRadius = 4,
        SpawnInterval = 20
    };
    public static GrenadeDefinition GetGrenadeDefinitionByType(GrenadeType type)
    {
        switch (type)
        {
            case GrenadeType.Fragmentation:
                return FragmentationGrenadeDefinition;
            case GrenadeType.Sticky:
                return StickyGrenadeDefinition;
            default:
                throw new System.NotImplementedException();
        }
    }

    public static OsFps Instance;
    
    public Server Server;
    public Client Client;

    [HideInInspector]
    public GameObject CanvasObject;

    #region Inspector-set Variables
    public GameObject PlayerPrefab;
    public GameObject CameraPrefab;

    public GameObject PistolPrefab;
    public GameObject SmgPrefab;

    public GameObject MuzzleFlashPrefab;

    public GameObject FragmentationGrenadePrefab;
    public GameObject FragmentationGrenadeExplosionPrefab;

    public GameObject StickyGrenadePrefab;
    public GameObject StickyGrenadeExplosionPrefab;

    public GameObject GUIContainerPrefab;
    public GameObject CrosshairPrefab;
    #endregion

    public ConnectionConfig CreateConnectionConfig(
        out int reliableSequencedChannelId,
        out int reliableChannelId,
        out int unreliableStateUpdateChannelId
    )
    {
        var connectionConfig = new ConnectionConfig();
        reliableSequencedChannelId = connectionConfig.AddChannel(QosType.ReliableSequenced);
        reliableChannelId = connectionConfig.AddChannel(QosType.Reliable);
        unreliableStateUpdateChannelId = connectionConfig.AddChannel(QosType.StateUpdate);

        return connectionConfig;
    }

    public GameObject GetWeaponPrefab(WeaponType weaponType)
    {
        switch (weaponType)
        {
            case WeaponType.Pistol:
                return PistolPrefab;
            case WeaponType.Smg:
                return SmgPrefab;
            default:
                throw new System.NotImplementedException("Unknown weapon type: " + weaponType);
        }
    }
    public GameObject GetGrenadePrefab(GrenadeType grenadeType)
    {
        switch (grenadeType)
        {
            case GrenadeType.Fragmentation:
                return FragmentationGrenadePrefab;
            case GrenadeType.Sticky:
                return StickyGrenadePrefab;
            default:
                throw new System.NotImplementedException("Unknown grenade type: " + grenadeType);
        }
    }
    public GameObject GetGrenadeExplosionPrefab(GrenadeType grenadeType)
    {
        switch (grenadeType)
        {
            case GrenadeType.Fragmentation:
                return FragmentationGrenadeExplosionPrefab;
            case GrenadeType.Sticky:
                return StickyGrenadeExplosionPrefab;
            default:
                throw new System.NotImplementedException("Unknown grenade type: " + grenadeType);
        }
    }

    public GameObject SpawnLocalPlayer(PlayerState playerState)
    {
        var playerObject = Instantiate(
            PlayerPrefab, playerState.Position, Quaternion.Euler(playerState.LookDirAngles)
        );

        var playerComponent = playerObject.GetComponent<PlayerComponent>();
        playerComponent.Id = playerState.Id;
        playerComponent.Rigidbody.velocity = playerState.Velocity;

        return playerObject;
    }
    public GameObject SpawnLocalWeaponObject(WeaponObjectState weaponObjectState)
    {
        var weaponPrefab = GetWeaponPrefab(weaponObjectState.Type);
        var weaponObject = Instantiate(
            weaponPrefab,
            weaponObjectState.RigidBodyState.Position,
            Quaternion.Euler(weaponObjectState.RigidBodyState.EulerAngles)
        );

        var weaponObjectComponent = weaponObject.GetComponent<WeaponComponent>();
        weaponObjectComponent.Id = weaponObjectState.Id;
        weaponObjectComponent.BulletsLeftInMagazine = weaponObjectState.BulletsLeftInMagazine;
        weaponObjectComponent.BulletsLeftOutOfMagazine = weaponObjectState.BulletsLeftOutOfMagazine;

        var rigidbody = weaponObjectComponent.Rigidbody;
        rigidbody.velocity = weaponObjectState.RigidBodyState.Velocity;
        rigidbody.angularVelocity = weaponObjectState.RigidBodyState.AngularVelocity;

        return weaponObject;
    }
    public GameObject SpawnLocalGrenadeObject(GrenadeState grenadeState)
    {
        var grenadePrefab = GetGrenadePrefab(grenadeState.Type);
        var grenadeObject = Instantiate(
            grenadePrefab,
            grenadeState.RigidBodyState.Position,
            Quaternion.Euler(grenadeState.RigidBodyState.EulerAngles)
        );

        var grenadeComponent = grenadeObject.GetComponent<GrenadeComponent>();
        grenadeComponent.Id = grenadeState.Id;

        var rigidbody = grenadeComponent.Rigidbody;
        rigidbody.velocity = grenadeState.RigidBodyState.Velocity;
        rigidbody.angularVelocity = grenadeState.RigidBodyState.AngularVelocity;

        return grenadeObject;
    }

    public GameObject FindPlayerObject(uint playerId)
    {
        return GameObject.FindGameObjectsWithTag(PlayerTag)
            .FirstOrDefault(go => go.GetComponent<PlayerComponent>().Id == playerId);
    }
    public GameObject FindWeaponObject(uint id)
    {
        var weaponComponent = FindObjectsOfType<WeaponComponent>()
            .FirstOrDefault(wc => wc.Id == id);

        return (weaponComponent != null) ? weaponComponent.gameObject : null;
    }
    public GameObject FindGrenade(uint id)
    {
        var grenadeComponent = FindObjectsOfType<GrenadeComponent>()
            .FirstOrDefault(g => g.Id == id);

        return (grenadeComponent != null) ? grenadeComponent.gameObject : null;
    }
    public PlayerComponent FindPlayerComponent(uint playerId)
    {
        var playerObject = FindPlayerObject(playerId);
        return (playerObject != null) ? playerObject.GetComponent<PlayerComponent>() : null;
    }
    public WeaponSpawnerComponent FindWeaponSpawnerComponent(uint id)
    {
        return FindObjectsOfType<WeaponSpawnerComponent>()
            .FirstOrDefault(wsc => wsc.Id == id);
    }

    public PlayerInput GetCurrentPlayersInput()
    {
        return new PlayerInput
        {
            IsMoveFowardPressed = Input.GetKey(MoveForwardKeyCode),
            IsMoveBackwardPressed = Input.GetKey(MoveBackwardKeyCode),
            IsMoveRightPressed = Input.GetKey(MoveRightKeyCode),
            IsMoveLeftPressed = Input.GetKey(MoveLeftKeyCode),
            IsFirePressed = Input.GetMouseButton(FireMouseButtonNumber)
        };
    }
    public Vector3 GetRelativeMoveDirection(PlayerInput input)
    {
        var moveDirection = Vector3.zero;

        if (input.IsMoveFowardPressed)
        {
            moveDirection += Vector3.forward;
        }

        if (input.IsMoveBackwardPressed)
        {
            moveDirection += Vector3.back;
        }

        if (input.IsMoveRightPressed)
        {
            moveDirection += Vector3.right;
        }

        if (input.IsMoveLeftPressed)
        {
            moveDirection += Vector3.left;
        }

        return moveDirection.normalized;
    }

    public void UpdatePlayerMovement(PlayerState playerState)
    {
        var playerComponent = FindPlayerComponent(playerState.Id);
        if (playerComponent == null) return;

        ApplyLookDirAnglesToPlayer(playerComponent, playerState.LookDirAngles);

        var relativeMoveDirection = GetRelativeMoveDirection(playerState.Input);
        playerComponent.Rigidbody.AddRelativeForce(1000 * relativeMoveDirection);
    }
    public void UpdatePlayer(PlayerState playerState)
    {
        // reload
        if (playerState.IsReloading)
        {
            playerState.ReloadTimeLeft -= Time.deltaTime;
        }

        // shot interval
        if ((playerState.CurrentWeapon != null) && (playerState.CurrentWeapon.TimeUntilCanShoot > 0))
        {
            playerState.CurrentWeapon.TimeUntilCanShoot -= Time.deltaTime;
        }

        // grenade throw interval
        if (playerState.TimeUntilCanThrowGrenade > 0)
        {
            playerState.TimeUntilCanThrowGrenade -= Time.deltaTime;
        }

        // update movement
        UpdatePlayerMovement(playerState);
    }

    public Vector2 GetPlayerLookDirAngles(PlayerComponent playerComponent)
    {
        return new Vector2(
            playerComponent.CameraPointObject.transform.localEulerAngles.x,
            playerComponent.transform.eulerAngles.y
        );
    }
    public void ApplyLookDirAnglesToPlayer(PlayerComponent playerComponent, Vector2 LookDirAngles)
    {
        playerComponent.transform.localEulerAngles = new Vector3(0, LookDirAngles.y, 0);
        playerComponent.CameraPointObject.transform.localEulerAngles = new Vector3(LookDirAngles.x, 0, 0);
    }

    // probably too much boilerplate here
    public void OnPlayerCollidingWithWeapon(GameObject playerObject, GameObject weaponObject)
    {
        if (Server != null)
        {
            PlayerSystem.Instance.ServerOnPlayerCollidingWithWeapon(Server, playerObject, weaponObject);
        }
    }

    public void OnPlayerCollidingWithGrenade(GameObject playerObject, GameObject grenadeObject)
    {
        if (Server != null)
        {
            PlayerSystem.Instance.ServerOnPlayerCollidingWithGrenade(playerObject, grenadeObject);
        }
    }

    public void GrenadeOnCollisionEnter(GrenadeComponent grenadeComponent, Collision collision)
    {
        if (Server != null)
        {
            GrenadeSystem.Instance.ServerGrenadeOnCollisionEnter(Server, grenadeComponent, collision);
        }
    }

    private void Awake()
    {
        // Destroy the game object if there is already an OsFps instance.
        if(Instance != null)
        {
            enabled = false;
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        GameObject guiContainer = Instantiate(GUIContainerPrefab);
        DontDestroyOnLoad(guiContainer);

        CanvasObject = guiContainer.FindDescendant("Canvas");
    }
    private void Start()
    {
        // Initialize & configure network.
        NetworkTransport.Init();
    }
    private void OnDestroy()
    {
        // Don't do anything if we're destroying a duplicate OsFps object.
        if (this != Instance)
        {
            return;
        }

        ShutdownNetworkPeers();
        NetworkTransport.Shutdown();
    }
    private void Update()
    {
        if (Server != null)
        {
            Server.Update();
        }

        if (Client != null)
        {
            Client.Update();
        }
    }
    private void LateUpdate()
    {
        if (Server != null)
        {
            Server.LateUpdate();
        }

        if (Client != null)
        {
            Client.LateUpdate();
        }
    }
    private void OnGUI()
    {
        if((Server == null) && (Client == null))
        {
            if(GUI.Button(new Rect(10, 10, 200, 30), "Connect To Server"))
            {
                SceneManager.sceneLoaded += OnMapLoadedAsClient;
                SceneManager.LoadScene("Test Map");
            }

            if (GUI.Button(new Rect(10, 50, 200, 30), "Start Server"))
            {
                Server = new Server();
                Server.Start();
            }
        }
        else
        {
            if (Client != null)
            {
                Client.OnGui();
            }
        }
    }

    private void ShutdownNetworkPeers()
    {
        if (Client != null)
        {
            Client.DisconnectFromServer();
            Client.Stop();

            Client = null;
        }

        if (Server != null)
        {
            Server.Stop();

            Server = null;
        }
    }

    private void OnMapLoadedAsClient(Scene scene, LoadSceneMode loadSceneMode)
    {
        SceneManager.sceneLoaded -= OnMapLoadedAsClient;

        Client = new Client();
        Client.OnDisconnectedFromServer += OnClientDisconnectedFromServer;
        Client.Start(true);
        Client.StartConnectingToServer(LocalHostIpv4Address, Server.PortNumber);
    }
    private void OnClientDisconnectedFromServer()
    {
        ShutdownNetworkPeers();
        SceneManager.LoadScene("Start");
    }
}