// One entry of PuppetMaster.muscles: it ties a physics body to the animated
// transform it should follow. Field names, order and element types are read from
// the typetree of a stock NPC's PuppetMaster — declaration order is what Unity
// serialises by, so it is not cosmetic.
using System;
using UnityEngine;
using SLZ.Marrow.Interaction;

namespace SLZ.Marrow.PuppetMasta
{
    [Serializable]
    public struct Muscle
    {
        [Serializable]
        public struct Props
        {
            public int group;
            public float mappingWeight;
            public float muscleWeight;
            public float muscleDamper;
            public byte mapPosition;
            public int[] ignoredMuscleIndexs;
        }

        public string name;
        public Transform target;
        public Props props;
        public int[] parentIndexes;
        public int[] childIndexes;
        public byte[] childFlags;
        public int[] kinshipDegrees;
        public MuscleCollisionBroadcasterSensor broadcaster;
        public UnityEngine.Object jointBreakBroadcaster;
        public Vector3 positionOffset;
        public Vector3 mappedVelocity;
        public Vector3 mappedAngularVelocity;
        public MarrowJoint marrowJoint;
        public MarrowBody marrowBody;
    }
}
