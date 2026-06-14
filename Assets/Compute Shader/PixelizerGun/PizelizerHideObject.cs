using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class PixelizerHideObject : MonoBehaviour
{
    public PixelizerDrawCubes _pixelizerScript;
    [Header("Pixelizer")]
    public bool _hideObject = false;
    public float pixelizeDuration = 5f;
    public float colorDuration = 1f;
    public int _enemyID = 0;
    [SerializeField] private ParticleSystem _dummyParticles;
    [ColorUsage(true, true)] public Color _glowcolor = Color.green;

    [Header("Animation")]
    [SerializeField] private Animator anim;

    [Header("Materials")]
    [SerializeField] private Material[] _noForwardLit;
    [SerializeField] private Material[] _forwardLit;

    [Header("Stats")]
    public int hp = 100;
    public Slider _hpBar;
    private SkinnedMeshRenderer _renderer;
    private Coroutine pixelRoutine;
    private Coroutine colorRoutine;
    private Coroutine delayPhysics;
    private bool isDead = false;

    private void Awake()
    {
        _renderer = GetComponent<SkinnedMeshRenderer>();
        ApplyMaterials();
        foreach(Material m in _noForwardLit)
        {
            m.SetInt("_EnemyID",_enemyID);            
        }
        _hpBar.value = 1.0f;
    }
    private void ApplyMaterials()
    {
        _renderer.materials = _hideObject ? _noForwardLit : _forwardLit;
    }

    public void TakeDamage()
    {
        if (isDead) return;

        hp -= 60;   
        _hpBar.value = hp / 100.0f;
        if (hp > 0)
        {
            PlayHitAnimation();
            StartPixelizeTemporary();
        }
        else
        {
            Die();
            _hpBar.gameObject.SetActive(false);
        }
        StartColorLerp();
    }
    private void StartColorLerp()
    {
        if (colorRoutine == null)
            StartCoroutine(ColorLerp(colorDuration));
        else
            StopCoroutine(ColorLerp(colorDuration));
    }
    private IEnumerator ColorLerp(float duration)
    {
        float t = 0f;

        Color start = _glowcolor;
        Color end = Color.black;

        while (t < duration)
        {
            t += Time.deltaTime;
            float lerp = t / duration;

            Color c = Color.Lerp(start, end, lerp);
            foreach (var mat in _noForwardLit)
                mat.SetColor("_Tint", c);
            yield return null;
        }
        foreach (var mat in _noForwardLit)
            mat.SetColor("_Tint", end);
    }

    private void PlayHitAnimation()
    {
        anim.SetBool("Hit", true);
        anim.SetBool("Die", false);
        StartCoroutine(ResetAnimBoolNextFrame("Hit"));
        _dummyParticles.Play();
    }

    private void Die()
    {
        isDead = true;

        anim.SetBool("Hit", false);
        anim.SetBool("Die", true);
        _dummyParticles.Play();
        _hideObject = true;
        ApplyMaterials();
        if (pixelRoutine != null)
            StopCoroutine(pixelRoutine);  
    }
    public void StartPhysics(){ 
        foreach(Material m in _noForwardLit){
            m.SetInt("_EnemyDead",1);            
        }
        GetComponent<CapsuleCollider>().enabled = false;
        _pixelizerScript.KillEnemy(_enemyID);           
    }

    private IEnumerator ResetAnimBoolNextFrame(string param)
    {
        yield return null;
        anim.SetBool(param, false);
    }

    private void StartPixelizeTemporary()
    {
        if (pixelRoutine != null)
            StopCoroutine(pixelRoutine);

        pixelRoutine = StartCoroutine(PixelizeForSeconds());
    }

    private IEnumerator PixelizeForSeconds()
    {
        _hideObject = true;
        ApplyMaterials();

        yield return new WaitForSeconds(pixelizeDuration);
        if (!isDead)
        {
            _hideObject = false;
            ApplyMaterials();
        }
    }
}
