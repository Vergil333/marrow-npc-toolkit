// Serialized layout only — the game's own class supplies the behaviour.
//
// `InteractableHost.VirtualController` is the grab system: it describes how a
// hand attaches to whatever it grabs — swing and twist limits of the grab
// joint, how strongly the hand follows position and rotation.
//
// Our InteractableHost stub declared no such field, so the whole struct was
// missing from our serialized data and arrived null on all 16 hosts. Anything
// walking it during a grab throws, and because the player's hands run one grab
// routine for everything, that breaks grabbing for the entire session rather
// than just for this NPC. It is also the most likely reason the head felt stiff
// and grabbed as an invisible ball: with no controller settings, the grab joint
// has no limits to honour.
//
// This is the fourth instance of one failure shape — a nested serializable
// field our stub never declared, deserialising as null instead of empty. See
// also MarrowEntity._behaviours, BehaviourPowerLegs.sensors and
// MarrowBody.trackerSettings.
//
// Values and layout are read off the stock NPC's typetree.

using System;
using UnityEngine;

namespace SLZ.Marrow.Interaction
{
    [Serializable]
    public class VirtualControllerSettings
    {
        public float lookRotationWeight;
        public float handTwistWeight;
        public float handSwingWeight;
        public float positionWeight;
        public float jointSwingLimit;
        public float jointTwistLimit;
        public bool autoTargetUpdatePrimary;
        public bool dynamicHandDistanceWeights;
    }

    [Serializable]
    public class VirtualControllerTransform
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    [Serializable]
    public class VirtualController
    {
        public VirtualControllerSettings defaultSettings;
        public VirtualControllerTransform overrideVCTransform;
    }
}
