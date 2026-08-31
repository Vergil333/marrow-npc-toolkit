// Serialized layout only — the game's own SLZ.Marrow class supplies behaviour.
//
// The complete layout is required here.  BehaviourPowerLegs reads the five
// UsageSettings directly from prefabConfig when it changes mental state; an
// asset authored with those fields missing deserializes as all-zero usage and
// disables every PuppetMaster muscle.  BaseEnemyConfig.ApplyTo also
// dereferences sensorSettings and healthSettings on NPC reactivation.

using System;
using UnityEngine;

namespace SLZ.Marrow.PuppetMasta
{
    // The public SDK omits this runtime type, but BaseEnemyConfig embeds its
    // value-type UsageSettings.  Declaring only the nested serialized layout is
    // sufficient for authoring; BONELAB binds to its own SLZ.Marrow assembly.
    public class SubBehaviourHealth
    {
        [Serializable]
        public struct UsageSettings
        {
            public float hips;
            public float spine;
            public float legLf;
            public float legRt;
            public float armLf;
            public float armRt;
        }
    }

    public class BaseEnemyConfig : ScriptableObject
    {
        [Serializable]
        public class SensorSettings
        {
            public LayerMask blockVisionRaycast;
            public float visionFov;
        }

        [Serializable]
        public class HealthSettings
        {
            public float maxHitPoints;
            public float maxAppendageHp;
            public float stunRecovery;
            public float maxStunSeconds;
            public float minHeadImpact;
            public float minSpineImpact;
            public float minLimbImpact;
            public float aggression;
            public float irritability;
            public float placability;
            public float vengefulness;
        }

        public string defaultSavePath;
        public int puppetState;
        public float restingRange;
        public bool freezeWhileResting;
        public bool homeIsPost;
        public float activeRange;
        public float roamSpeed;
        public Vector2 roamRange;
        public float roamAngSpeed;
        public float roamFrequency;
        public bool roamWanders;
        public float investigateRange;
        public float breakAgroTargetDistance;
        public float breakAgroHomeDistance;
        public float agroedSpeed;
        public float agroedAngSpeed;
        public LayerMask meleeAttackMask;
        public bool enableThrowAttack;
        public float throwMaxRange;
        public float throwMinRange;
        public float throwCooldown;
        public float throwVelocity;
        public float gunRange;
        public float gunCooldown;
        public float accuracy;
        public float reloadTime;
        public int clipSize;
        public int burstSize;
        public float desiredGunDistance;
        public Color baseColor;
        public Color agroColor;
        public float fwdThresh;
        public bool forcePath;
        public SensorSettings sensorSettings = new SensorSettings();
        public HealthSettings healthSettings = new HealthSettings();
        public SubBehaviourHealth.UsageSettings restingUsage;
        public SubBehaviourHealth.UsageSettings roamUsage;
        public SubBehaviourHealth.UsageSettings investigateUsage;
        public SubBehaviourHealth.UsageSettings engagedUsage;
        public SubBehaviourHealth.UsageSettings agroedUsage;
        public int locoState;
    }
}
