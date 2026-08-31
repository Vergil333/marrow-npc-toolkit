// PuppetMaster.stateSettings and .solvers. Absent fields arrive as zero or null
// at runtime, and a PuppetMaster whose angular limits are off and whose muscle
// weights are zero takes the ragdoll over and leaves it inert — which is the
// T-posing, pass-through NPC. Layout and element type read from a stock NPC.
using System;

namespace SLZ.Marrow.PuppetMasta
{
    // Reference type only, so `solvers` serialises as PPtr<$SolverManager>.
    public class SolverManager : UnityEngine.MonoBehaviour { }

    [Serializable]
    public struct StateSettings
    {
        public float killDuration;
        public float deadMuscleWeight;
        public float deadMuscleDamper;
        public float maxFreezeSqrVelocity;
        public byte enableAngularLimitsOnKill;
        public byte enableInternalCollisionsOnKill;
    }
}
