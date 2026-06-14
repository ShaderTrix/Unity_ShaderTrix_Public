using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Unity.Properties;
using UnityEditor;
using UnityEngine;

namespace SoftBodySimulation
{
    public sealed class MassSpringClothConfiguration
    {
        public uint SolverIterations { get; set; }
        public uint DeltaTimeDivisor { get; set; }
        public float GravityMultiplier { get; set; }
        public float ElasticSpringConstant { get; set; }
        public float ShearSpringConstant { get; set; }
        public float BendSpringConstant { get; set; }
        public float MeshBasedElasticSpringConstant { get; set; }
        public float MeshBasedShearSpringConstant { get; set; }
        public float SpringInverseMass { get; set; }
        public float RestitutionConstant { get; set; }
        public float FrictionConstant { get; set; }
        public float SpringDamping { get; set; }
        public float WindStrengthMultiplier { get; set; }
        public float WindNoiseScaleMultiplier { get; set; }
        public Vector3 Gravity => Physics.clothGravity * GravityMultiplier;
        public ComputeShader _computeShader;
        public int kernelID;
        public int groupSize;
        public static ComputeShader FindComputeShader(string name)
        {
            string[] guids = AssetDatabase.FindAssets($"{name} t:ComputeShader");
            string path;
            if (guids.Length > 0)
            {
                path = AssetDatabase.GUIDToAssetPath(guids[0]);
                return AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            }
            else
                throw new InvalidDataException($"{name} is Invalid,could not find shader");
        }
        public float SpringConstantForTypes(SpringDamperType type)
        {
            return type switch
            {
                SpringDamperType.Elastic => ElasticSpringConstant,
                SpringDamperType.Shear => ShearSpringConstant,
                SpringDamperType.Bend => BendSpringConstant,
                SpringDamperType.MeshElastic => MeshBasedElasticSpringConstant,
                SpringDamperType.MeshShear => MeshBasedShearSpringConstant,
                _ => throw new InvalidEnumArgumentException()
            };
        }
    }
}
