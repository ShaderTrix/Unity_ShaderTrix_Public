namespace SoftBodySimulation
{
    /// <summary>
    /// Baxter gripper that will automatically attach to the closest attachable point.
    /// </summary>
    public class AutomaticBaxterHandGrab : BaxterHandGrab
    {
        private void FixedUpdate()
        {
            if (isOn)
            {
                if(!IsAttached)
                    AttachToClosest();
            }
            else
            {
                if(IsAttached){
                    Detach();
                    IsAttached = false;
                }
            }
        }     
    }
}