using UnityEngine;

public class PixelizerAnimationEventListener : MonoBehaviour
{
    [SerializeField]private PixelizerHideObject _hideObjectsScript;
    public void PhysicsCubes()
    {
        _hideObjectsScript.StartPhysics();
    }
}
