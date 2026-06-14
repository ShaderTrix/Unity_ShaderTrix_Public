using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace SoftBodySimulation
{
     public class PBDBaseCloth : MonoBehaviour
    {
        [SerializeField] protected GameObject _bonePrefab;
        [SerializeField, Range(1, 32)] protected int _iterations = 8;        
        [SerializeField, Range(0.01f, 1.0f)] protected float _edgeLength = 0.1f;
        [SerializeField, Range(0.85f, 1f)] protected float _decay = 1f;        
        protected int nodesCount;
        [SerializeField]protected Vector3 _gravity = new Vector3(0,-1,0);
        protected int damperCount;
        protected IClothPBDProcessor _pbdProcessor;
        protected Transform[] Bones;
        protected SkinnedMeshRenderer skm => GetComponent<SkinnedMeshRenderer>();
        protected PBDNode[] nodes;
        protected List<PBDDamper> dampers;
        protected PBDPin[] pins;
        [SerializeField] protected Transform[] _pinTransform;
        [SerializeField] private float _pinDist = 0.1f;
        protected virtual void Start()
        {
        }
        protected virtual void Update()
        {
            _pbdProcessor.Pinned(_pinTransform,_pinDist);
            _pbdProcessor.Gravity(_gravity);
            _pbdProcessor.Step(_gravity,_decay); 
            for(int i = 0; i < _iterations; i++)
            {
                _pbdProcessor.Solve();
            }    
            _pbdProcessor.SyncBonesOnCPU(Bones);
        }        
        protected void Initialize(Mesh mesh, Transform[] bones)
        {            
            Bones = bones;

            mesh.bindposes = CalculateBindPosesFromTransforms(bones);

            skm.bones = bones;
            var bounds = mesh.bounds;
            var middleBone = bones[bones.Length / 2];
            bounds.center += transform.position - middleBone.position;
            skm.localBounds = bounds;
            skm.rootBone = middleBone;
            skm.sharedMesh = mesh;            
        }      
        private Matrix4x4[] CalculateBindPosesFromTransforms(IEnumerable<Transform> boneTransforms)
        {
            var localToWorldMatrix = transform.localToWorldMatrix;
            return (from boneTransform in boneTransforms select boneTransform.worldToLocalMatrix * localToWorldMatrix)
                .ToArray();
        }
        protected virtual void OnDestroy() => _pbdProcessor?.Dispose();
        public Transform[] GetBones() { return Bones; }    
    }
}

