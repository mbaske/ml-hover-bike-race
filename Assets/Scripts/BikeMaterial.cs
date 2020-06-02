using UnityEngine;

namespace Hoverbike
{
    public class BikeMaterial : MonoBehaviour
    {
        [SerializeField]
        private Material bikeMaterial;

        private void OnValidate()
        {
            if (bikeMaterial != null)
            {
                Renderer[] renderers = GetComponentsInChildren<Renderer>();
                foreach (Renderer r in renderers)
                {
                    r.material = bikeMaterial;
                }
            }
        }
    }
}