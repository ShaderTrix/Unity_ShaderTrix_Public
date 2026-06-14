using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DecalSizeHandler : MonoBehaviour
{
    public enum DamageZone
    {
        Core,
        Limb,
        Environment,
    }
    [SerializeField]public DamageZone zone = DamageZone.Core;

    [SerializeField] private List<Texture2D> _bloodDecal;
    [SerializeField] private List<Texture2D> _bulletDecal;
    public float GetZoneScale()
    {
        switch (zone)
        {
            case DamageZone.Core:  return 0.2f;
            case DamageZone.Limb:  return 0.5f;
            case DamageZone.Environment:  return 0.5f;
            default: return 1.0f;
        }
    }
    public Texture2D GetZoneTexture(int n)
    {
        switch(zone)
        {
            case DamageZone.Core: return _bloodDecal[n];
            case DamageZone.Limb: return _bloodDecal[n];
            case DamageZone.Environment: return _bloodDecal[n];
            default : return _bloodDecal[n];
        }
    }
    public Texture2D GetRandomZoneTexture()
    {
        switch(zone)
        {
            // case DamageZone.Core: return _bloodDecal[Random.Range(0, _bloodDecal.Count)];
            // case DamageZone.Limb: return _bloodDecal[Random.Range(0, _bloodDecal.Count)];
            // case DamageZone.Environment: return _bulletDecal[Random.Range(0, _bulletDecal.Count)];
            // default : return _bulletDecal[Random.Range(0, _bulletDecal.Count)];
            case DamageZone.Core: return _bloodDecal[Random.Range(0, _bloodDecal.Count)];
            case DamageZone.Limb: return _bloodDecal[Random.Range(0, _bloodDecal.Count)];
            case DamageZone.Environment: return _bloodDecal[Random.Range(0, _bloodDecal.Count)];
            default : return _bloodDecal[Random.Range(0, _bloodDecal.Count)];
        }
    }
}
