// The finger pose data a HandPose carries. Declared because HandPose.poseData is
// a PoseDataGroup[], and without it Unity drops that field when importing a hand
// pose asset — so every pose we shipped was gutted on import, and
// HandPoseAnimator.GetHandleInHandNeutral threw reading it. That exception fired
// every frame a hand hovered, which is what stopped grabbing from working.
//
// Field order is not cosmetic: Unity lays a struct out in declaration order, and
// the layout has to match the game's. Read from the typetree of a stock hand
// pose asset, not chosen.
using System;
using UnityEngine;
using SLZ.Marrow.Utilities;

namespace SLZ.Marrow
{
    [Serializable]
    public struct PoseData
    {
        public Vector3 nativePry;
        public Quaternion thumb1;
        public Quaternion index1;
        public Quaternion middle1;
        public Quaternion ring1;
        public Quaternion pinky1;
        public float thumb2;
        public float thumb3;
        public float index2;
        public float index3;
        public float middle2;
        public float middle3;
        public float ring2;
        public float ring3;
        public float pinky2;
        public float pinky3;
        public SimpleTransform leftHandle;
        public SimpleTransform invLeftHandle;
        public SimpleTransform rightHandle;
        public SimpleTransform invRightHandle;
        public SimpleTransform leftArtHandle;
        public SimpleTransform rightArtHandle;
    }

    [Serializable]
    public struct PoseDataGroup
    {
        public float radius;
        public PoseData[] poseArray;
    }
}
