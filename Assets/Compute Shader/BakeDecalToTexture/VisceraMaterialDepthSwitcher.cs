using System.Collections;
using UnityEngine;

public class VisceraMaterialDepthSwitcher : MonoBehaviour
{
    Material mat;
    Coroutine bakeCoroutine;

    void Awake()
    {
        mat = GetComponent<MeshRenderer>().sharedMaterial;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.C))
        {
            StartBakeRequest();
        }
    }

    void StartBakeRequest()
    {
        if (bakeCoroutine != null)
        {
            StopCoroutine(bakeCoroutine);
        }

        bakeCoroutine = StartCoroutine(BakeRequest());
    }

    IEnumerator BakeRequest()
    {
        mat.SetFloat("_ZWriteMode", 0);
        Debug.Log("Scitching ZWrite mode");
        yield return new WaitForSeconds(0.2f);

        mat.SetFloat("_ZWriteMode", 1);

        bakeCoroutine = null;
    }
}
