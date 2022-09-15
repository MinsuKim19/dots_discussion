using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Experimental.AI;
using UnityEngine.InputSystem;

public class DefenseManager : MonoBehaviour
{
    public Camera mainCam;
    public DefenseSetting defenseSetting;
    public Transform EnemySpawnPoint;
    public float SpawnAllyCoolTime = 1f;

    EntityManager EntityManager;

    TextureAnimatorSystem textureAnimatorSystem;

    public void Awake()
    {
        DefenseSetting.SetInstance(defenseSetting);

        EntityManager = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
        var navMeshWorld = NavMeshWorld.GetDefaultWorld();

        textureAnimatorSystem = Unity.Entities.World.DefaultGameObjectInjectionWorld.GetExistingSystem<TextureAnimatorSystem>();
    }

    public void Update()
    {
        SpawnAllyCoolTime -= Time.deltaTime;

        if(SpawnAllyCoolTime < 0)
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            var mouse = Mouse.current;

            if (mouse.leftButton.IsPressed())
            {
                Vector2 position = mouse.position.ReadValue();
#else
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                Vector3 position2 = touch.position;
#endif
                RaycastHit hit;
                Ray ray = mainCam.ScreenPointToRay(position);

                if (Physics.Raycast(ray, out hit))
                {
                    var spawner = new GameObject();
                    SpawnerAllyAuthoring saa = spawner.AddComponent<SpawnerAllyAuthoring>();
                    saa.Prefab = DefenseSetting.Instance.AllyPrefab;
                    saa.SpawnPosition = hit.point;
                    saa.Count = 1;
                    spawner.AddComponent<ConvertToEntity>();

                    SpawnAllyCoolTime = 1f;
                }
            }
        }
    }
}
