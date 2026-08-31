// NOTE: derives from MarrowBehaviour, not MonoBehaviour. MarrowEntity._behaviours
// is a MarrowBehaviour[]; while these stubs derived from MonoBehaviour, Unity
// silently refused every assignment into that array and shipped 17 nulls.
// Generated stub — declarations only, no behaviour.
//
// BONELAB binds this prefab component by the triple (SLZ.Marrow.dll,
// SLZ.Marrow.PuppetMasta, PuppetMaster). The public Marrow SDK omits the type, so a
// declaration has to exist here for Unity to author against; at runtime
// the game's own class supplies the behaviour and Unity's typetree
// remapping fills these fields by name.
//
// Field names AND ORDER now mirror the shipped Patch 6 layout exactly, taken
// from the stock NPC bundle's typetree (peasantfemaleb.bundle) and
// cross-checked against a field-level decompile of SLZ.Marrow's PuppetMaster.
// Unity's safe binary read matches by name, so order should not matter — but
// this has cost enough guesswork already that exact parity is worth having.
//
// `humanoidConfig` was the one field missing here. It is null on the stock NPC
// too, so it is declared and left unassigned purely for layout parity.

namespace SLZ.Marrow.PuppetMasta
{
    public class PuppetMaster : SLZ.Marrow.Interaction.MarrowBehaviour
    {
        public SLZ.Marrow.Interaction.MarrowEntity marrowEntity;
        public SLZ.Marrow.Pool.Poolee _poolee;
        public PuppetMasterHumanoidConfig humanoidConfig;
        public UnityEngine.Transform targetRoot;
        public int state;
        public StateSettings stateSettings;
        public int mode;
        public float blendTime;
        public int solverIterationCount;
        public byte visualizeTargetAnimation;
        public byte visualizeTargetPose;
        public float mappingWeight;
        public float muscleWeight;
        public float muscleSpring;
        public float muscleDamper;
        public byte updateJointAnchors;
        public byte angularLimits;
        public byte internalCollisions;
        public SLZ.Marrow.PuppetMasta.Muscle[] muscles;
        public byte cullAnimators;
        public UnityEngine.Animator[] cullableAnimators;
        public SolverManager[] solvers;
    }
}
