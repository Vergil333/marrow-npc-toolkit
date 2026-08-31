// GENERATED from a shipped bundle's typetree — declarations only, no behaviour.
//
// Do not hand-trim this. A field omitted here is absent from our serialized
// data, so the game's class gets null instead of an empty collection, and the
// first code to touch it throws. That is what a hand-written partial stub of
// this class caused: 120 of 379 leaves present, and initialisation dying in a
// different nested struct on every build.
//
// Regenerate: _pipeline/npc/stub_from_typetree.py <bundle> <monoscripts> LiteLoco <out.cs> SLZ.Marrow.Mechanics

using UnityEngine;
using UnityEngine.AI;
using SLZ.Marrow;
using SLZ.Marrow.AI;
using SLZ.Marrow.Audio;
using SLZ.Marrow.Combat;
using SLZ.Marrow.Data;
using SLZ.Marrow.Interaction;
using SLZ.Marrow.Mechanics;
using SLZ.Marrow.Pool;
using SLZ.Marrow.PuppetMasta;
using SLZ.Marrow.Warehouse;

namespace SLZ.Marrow.Mechanics
{
    [System.Serializable]
    public class StepGroup
    {
        public Transform pelvis;
        public int sisterStepGroup;
        public float legLength;
        public UnityEngine.AnimationCurve FootXVCurve;
        public int _gear;
        public byte computeAnimCycle;
        public byte visualizeAnimCycle;
        public float animCycle;
        public Gear[] gears;
        public Grounder grounder;
        public Footstep[] footsteps;
    }

    [System.Serializable]
    public class Gear
    {
        public float upshiftVel;
        public float downshiftVel;
        public float stepProgressThreshold;
        public float stepfromtoWeight;
        public float minStepThreshold;
        public UnityEngine.AnimationCurve StepRateVCurve;
        public UnityEngine.AnimationCurve stepHeight;
        public UnityEngine.AnimationCurve StepZInterp;
        public UnityEngine.AnimationCurve StepAnkleBend;
        public UnityEngine.AnimationCurve MuscleUsage;
    }

    [System.Serializable]
    public class Grounder
    {
        // The shipped typetree stores this as LayerMask { m_Bits }, not a
        // scalar int. Using int silently deserializes the native mask as zero.
        public LayerMask layers;
        public float maxStep;
        public float footSpeed;
    }

    [System.Serializable]
    public class Footstep
    {
        public Transform hip;
        public Transform foot;
        public Transform neutralTarget;
        public float rotationOffset;
        public Collider footCollider;
        public PhysicMaterial liftedMat;
        public FootstepSFX stepSfx;
    }

    public class LiteLoco : UnityEngine.MonoBehaviour
    {
        public float weight;
        public Transform root;
        public Transform neutralRoot;
        public StepGroup[] stepGroups;
    }
}
