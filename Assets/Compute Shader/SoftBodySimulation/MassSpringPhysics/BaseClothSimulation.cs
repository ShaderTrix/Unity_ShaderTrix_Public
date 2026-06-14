using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEngine;

namespace SoftBodySimulation
{
    //this class is abstract because we need monobehaviours Start()/update()etc
    //but dont want anyone to directly assign this class to any object 
    public abstract class BaseClothSimulation : MonoBehaviour
    {
        [Header("Colliders")]
        [SerializeField] private SphereCollider[] _sphereColliders;
        [SerializeField] private BoxCollider[] _boxColliders;
        [SerializeField] private GameObject _windDirection;

        [Header("Configuration")]
        [SerializeField] protected GameObject _bonePrefab;
        private ComputeShader _computeShader;

        [Header("Parameters")]
        [SerializeField] private uint _solverIterations = 3;
        [SerializeField] private uint _deltaTimeDivisor = 20;
        [SerializeField] private float _gravityMultiplier = 0.25f;
        [SerializeField] private float _elasticSpringResistance = 5400f;
        [SerializeField] private float _shearSpringResistance = 3000f;
        [SerializeField] private float _bendSpringResistance = 2400f;
        [SerializeField] private float _meshBasedElasticSpringResistance = 2400f;
        [SerializeField] private float _meshBasedShearSpringResistance = 2400f;
        [SerializeField] private float _springInverseMass = 0.125f;
        [SerializeField, Range(10f, 200f)] private float _springDamping = 38f;
        [SerializeField, Range(0, 1)] private float _restitutionConstant = 0.02f;
        [SerializeField, Range(0, 1)] private float _frictionConstant = 0.95f;

        [Header("WindGlobalConfig")]
        [SerializeField] private float _windForceMultiplier = 4.0f;
        [SerializeField] private float _windNoiseScaleMultiplier = 1.0f;
        protected IClothSpringProcessor _springProcessor;
        private bool _valuesUpdated = false;
        protected Transform[] Bones;
        MassSpringClothConfiguration config;
        protected virtual void Start()
        {
            config = new MassSpringClothConfiguration
            {
                SolverIterations = _solverIterations,
                GravityMultiplier = _gravityMultiplier,
                FrictionConstant = _frictionConstant,
                RestitutionConstant = _restitutionConstant,
                SpringDamping = _springDamping,
                SpringInverseMass = _springInverseMass,
                MeshBasedElasticSpringConstant = _meshBasedElasticSpringResistance,
                MeshBasedShearSpringConstant = _meshBasedShearSpringResistance,
                BendSpringConstant = _bendSpringResistance,
                ShearSpringConstant = _shearSpringResistance,
                ElasticSpringConstant = _elasticSpringResistance,
                // IntegrationType = integrationType,
                DeltaTimeDivisor = _deltaTimeDivisor,
                WindStrengthMultiplier = _windForceMultiplier,
                WindNoiseScaleMultiplier = _windNoiseScaleMultiplier,                
            };                  
            _computeShader = MassSpringClothConfiguration.FindComputeShader("MassSpringCompute");
            _springProcessor = new GpuClothSpringProcessor(config, Instantiate(_computeShader));
        }
        private void UpdateConfigImmediately()
        {            
            config.GravityMultiplier = _gravityMultiplier;
            config.FrictionConstant = _frictionConstant;
            config.RestitutionConstant = _restitutionConstant;
            config.SpringDamping = _springDamping;
            config.SpringInverseMass = _springInverseMass;
            config.MeshBasedElasticSpringConstant = _meshBasedElasticSpringResistance;
            config.MeshBasedShearSpringConstant = _meshBasedShearSpringResistance;
            config.BendSpringConstant = _bendSpringResistance;
            config.ShearSpringConstant = _shearSpringResistance;
            config.ElasticSpringConstant = _elasticSpringResistance;
        }
        /// <summary>
        /// Initializes the cloth simulation.
        /// </summary>
        /// <param name="mesh">The cloth mesh.</param>
        /// <param name="bones">The transforms of the bones.</param>
        protected void Initialize(Mesh mesh, Transform[] bones)
        {
            _springProcessor.FinishInitialization();

            Bones = bones;

            mesh.bindposes = CalculateBindPosesFromTransforms(bones);

            var skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
            skinnedMeshRenderer.bones = bones;
            var bounds = mesh.bounds;
            var middleBone = bones[bones.Length / 2];
            bounds.center += transform.position - middleBone.position;
            skinnedMeshRenderer.localBounds = bounds;
            skinnedMeshRenderer.rootBone = middleBone;
            skinnedMeshRenderer.sharedMesh = mesh;
        }

        /// <summary>
        /// Enumerates spring nodes nearby a given sphere.
        /// </summary>
        /// <param name="centroid">The sphere centroid.</param>
        /// <param name="radius">The sphere radius.</param>
        /// <returns>An enumerable for the nearby spring nodes.</returns>
        public IEnumerable<ISpringNode> EnumerateNearbySphere(Vector3 centroid, float radius)
        {
            return _springProcessor.EnumerateNearbySphere(centroid, radius);
        }

        /// <summary>
        /// Calculates the bind pose matrices from the bone transforms, which will be the inverse transformed positions.
        /// </summary>
        /// <param name="boneTransforms">An iterable over the bone transforms.</param>
        /// <returns>The bind pose matrices.</returns>
        private Matrix4x4[] CalculateBindPosesFromTransforms(IEnumerable<Transform> boneTransforms)
        {
            // Bone pose wants to have the inverse transformed position.
            var localToWorldMatrix = transform.localToWorldMatrix;
            return (from boneTransform in boneTransforms select boneTransform.worldToLocalMatrix * localToWorldMatrix)
                .ToArray();
        }
        private void OnValidate()
        {
             if (Application.isPlaying)
                    _valuesUpdated = true; 
        }
        private void RefreshedBuffers()
        {
            // if (!_valuesUpdated) return;
            if (_springProcessor == null) return;
            if (config == null) return;

            // Debug.Log("Buffers updated");
            UpdateConfigImmediately(); 
            _springProcessor.UpdateSpringDamperConstants();
            // _valuesUpdated = false;
        }
        private void RefreshExternalForces()
        {
            if(!_windDirection || !_windDirection.activeSelf) return;
            if(!_windDirection.transform.hasChanged) return;
            if(_springProcessor == null) return;
            
            Debug.Log("External forces updated");
            _springProcessor.UpdateExternalForces(_windDirection.transform,_windForceMultiplier,_windNoiseScaleMultiplier);
        }
        private void FixedUpdate()
        {        
            RefreshedBuffers();
            RefreshExternalForces();

            var sphereCollisionProxies = new NativeArray<ImmovableSphereCollisionAdapter>(_sphereColliders.Length,
                Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            var cuboidCollisionProxies = new NativeArray<ImmovableCuboidCollisionAdapter>(_boxColliders.Length,
                Allocator.Temp, NativeArrayOptions.UninitializedMemory);

            for (var i = 0; i < _sphereColliders.Length; ++i)
                sphereCollisionProxies[i] = new ImmovableSphereCollisionAdapter(_sphereColliders[i]);

            for (var i = 0; i < _boxColliders.Length; ++i)
                cuboidCollisionProxies[i] = new ImmovableCuboidCollisionAdapter(_boxColliders[i]);

            // Since we need an order inside FixedUpdate we can't just use the unordered execution order of 
            // FixedUpdate on different game objects.
            var deltaTime = Time.fixedDeltaTime;
            _springProcessor.FixedUpdate(deltaTime, sphereCollisionProxies, cuboidCollisionProxies);

#if R_KEY_RESETS_CLOTH
            if (Input.GetKeyDown(KeyCode.R))
            {
                ResetToInitialState();
            }
#endif

            cuboidCollisionProxies.Dispose();
            sphereCollisionProxies.Dispose();
        }

#if DEBUG_GIZMOS
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.red;
            SpringProcessor?.OnDrawGizmos();
        }
#endif

        private void OnDestroy()
        {
            _springProcessor.OnDestroy();
        }

        /// <summary>
        /// Gets the bones of the cloth.
        /// </summary>
        /// <returns>The bones array.</returns>
        public Transform[] GetBones()
        {
            return Bones;
        }

        /// <summary>
        /// Resets the cloth to its initial start position
        /// </summary>
        public void ResetToInitialState()
        {
            _springProcessor.ResetToInitialState();
        }
    }
}
