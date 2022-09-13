using System;
using UnityEngine;

[CreateAssetMenu(fileName = "Data", menuName = "ScriptableObjects/DefenseSetting", order = 1)]
public class DefenseSetting : ScriptableObject
{
    public GameObject AllyPrefab;
    public GameObject EnemyPrefab;

    public GameObject AllyBakingPrefab;
    public GameObject EnemyBakingPrefab;
    public static void SetInstance(DefenseSetting ds)
    {
        instance.SetTarget(ds);
    }

    private static System.WeakReference<DefenseSetting> instance = new WeakReference<DefenseSetting>(null);
    public static DefenseSetting Instance
    {
        get
        {
            DefenseSetting ds = null;
            if (instance.TryGetTarget(out ds))
            {
                return ds;
            }
            return null;
        }
    }
}
