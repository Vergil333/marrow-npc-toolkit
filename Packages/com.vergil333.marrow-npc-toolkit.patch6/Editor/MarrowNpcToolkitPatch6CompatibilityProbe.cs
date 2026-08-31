using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using Vergil333.MarrowNpcToolkit;
using Vergil333.MarrowNpcToolkit.Editor.Build;
using Vergil333.MarrowNpcToolkit.Editor.Compatibility;
using Vergil333.MarrowNpcToolkit.Editor.Movement;

namespace Vergil333.MarrowNpcToolkit.ProjectCompatibility
{
    /// <summary>
    /// Exact-schema bridge between the public toolkit and separately installed
    /// BONELAB Patch 6 declarations. This only inspects loaded metadata; it does
    /// not create components, open prefabs, or modify assets.
    /// </summary>
    [InitializeOnLoad]
    internal sealed partial class MarrowNpcToolkitPatch6CompatibilityProbe :
        INpcNativeBuildProvider,
        INpcMovementAuthoringProvider
    {
        private const string MarrowAssembly = "SLZ.Marrow";
        private const string GameAssembly = "Assembly-CSharp";

        private static readonly MarrowNpcToolkitPatch6CompatibilityProbe Instance =
            new MarrowNpcToolkitPatch6CompatibilityProbe();

        static MarrowNpcToolkitPatch6CompatibilityProbe()
        {
            NpcCompatibilityProbeRegistry.Default.Register(Instance);
            NpcNativeBuildProviderRegistry.Default.Register(Instance);
        }

        private MarrowNpcToolkitPatch6CompatibilityProbe()
        {
        }

        public string ProviderId => "vergil333.bonelab-patch6";
        public string DisplayName => "BONELAB Patch 6 Provider";
        public string CompatibilityProfileId =>
            NpcToolkitVersion.InitialCompatibilityProfile;

        public NpcCompatibilityProbeResult Probe()
        {
            CapabilityAudit[] audits =
            {
                ProbeCoreAnatomy(),
                ProbeAi(),
                ProbePooling(),
                ProbeGrips(),
                ProbeGaze(),
                ProbeJawAndFace(),
                ProbeAudio(),
            };

            CapabilityAudit core = audits[0];
            CapabilityAudit ai = audits[1];
            CapabilityAudit pooling = audits[2];
            CapabilityAudit grips = audits[3];
            CapabilityAudit gaze = audits[4];
            CapabilityAudit jaw = audits[5];
            CapabilityAudit audio = audits[6];
            bool behaviourDeclarationsAvailable = core.IsAvailable
                                                  && ai.IsAvailable
                                                  && pooling.IsAvailable;
            bool behaviourProfileAvailable = false;
            string behaviourProfileDetail =
                "Behaviour profile preflight was skipped because one or more "
                + "required Patch 6 declarations are unavailable.";
            if (behaviourDeclarationsAvailable)
                behaviourProfileAvailable = TryPreflightBehaviourBuild(
                    out behaviourProfileDetail);
            bool behaviourBuilderAvailable = behaviourDeclarationsAvailable
                                             && behaviourProfileAvailable;
            bool gripProfileAvailable = false;
            string gripProfileDetail =
                "Body-grab preflight was skipped because the coupled behavior "
                + "builder or Patch 6 grip declarations are unavailable.";
            if (behaviourBuilderAvailable && grips.IsAvailable)
                gripProfileAvailable = TryPreflightGripBuild(out gripProfileDetail);
            bool gripBuilderAvailable = behaviourBuilderAvailable
                                        && grips.IsAvailable
                                        && gripProfileAvailable;
            bool gazeProfileAvailable = false;
            string gazeProfileDetail =
                "Gaze preflight was skipped because the coupled behavior "
                + "builder or Patch 6 gaze declarations are unavailable.";
            if (behaviourBuilderAvailable && gaze.IsAvailable)
                gazeProfileAvailable = TryPreflightGazeBuild(
                    out gazeProfileDetail);
            bool gazeBuilderAvailable = behaviourBuilderAvailable
                                        && gaze.IsAvailable
                                        && gazeProfileAvailable;
            bool jawProfileAvailable = false;
            string jawProfileDetail =
                "Physical Jaw preflight was skipped because the coupled behavior "
                + "builder or exact Jaw declarations are unavailable.";
            if (behaviourBuilderAvailable && jaw.IsAvailable)
                jawProfileAvailable = TryPreflightJawBuild(out jawProfileDetail);
            bool jawBuilderAvailable = behaviourBuilderAvailable
                                       && jaw.IsAvailable
                                       && jawProfileAvailable;
            bool audioProfileAvailable = false;
            string audioProfileDetail =
                "Audio provider preflight was skipped because the coupled "
                + "behavior builder or Patch 6 audio declarations are unavailable.";
            if (behaviourBuilderAvailable && audio.IsAvailable)
                audioProfileAvailable = TryPreflightAudioBuild(
                    out audioProfileDetail);
            bool audioBuilderAvailable = behaviourBuilderAvailable
                                         && audio.IsAvailable
                                         && audioProfileAvailable;
            string detail = FormatDetail(
                audits,
                core.IsAvailable,
                behaviourBuilderAvailable,
                behaviourProfileDetail,
                gripBuilderAvailable,
                gripProfileDetail,
                gazeBuilderAvailable,
                gazeProfileDetail,
                jawBuilderAvailable,
                jawProfileDetail,
                audioBuilderAvailable,
                audioProfileDetail);
            return !core.IsAvailable
                ? NpcCompatibilityProbeResult.Unavailable(detail)
                : NpcCompatibilityProbeResult.Available(
                    NpcCompatibilityCapabilities.CoreAnatomy
                    | (behaviourBuilderAvailable
                        ? NpcCompatibilityCapabilities.AI
                          | NpcCompatibilityCapabilities.Pooling
                          | (gripBuilderAvailable
                              ? NpcCompatibilityCapabilities.Grips
                              : NpcCompatibilityCapabilities.None)
                          | (gazeBuilderAvailable
                              ? NpcCompatibilityCapabilities.Gaze
                              : NpcCompatibilityCapabilities.None)
                          | (jawBuilderAvailable
                              ? NpcCompatibilityCapabilities.Jaw
                              : NpcCompatibilityCapabilities.None)
                          | (audioBuilderAvailable
                              ? NpcCompatibilityCapabilities.Audio
                              : NpcCompatibilityCapabilities.None)
                          | NpcCompatibilityCapabilities.SecondaryMotion
                        : NpcCompatibilityCapabilities.None),
                    detail);
        }

        private static CapabilityAudit ProbeCoreAnatomy()
        {
            var audit = new CapabilityAudit(
                NpcCompatibilityCapabilities.CoreAnatomy,
                "Core anatomy");

            Type marrowBody = audit.RequireType(
                "SLZ.Marrow.Interaction.MarrowBody", MarrowAssembly);
            audit.RequireBaseType(marrowBody, "UnityEngine.MonoBehaviour");
            audit.RequireField(marrowBody, "_defaultRigidbodyInfo",
                "SLZ.Marrow.Interaction.RigidbodyInfo");
            audit.RequireField(marrowBody, "_bounds", "UnityEngine.Bounds");
            audit.RequireField(marrowBody, "trackerSettings",
                "SLZ.Marrow.Interaction.TrackerSettings");
            audit.RequireField(marrowBody, "<InitInEntityTransform>k__BackingField",
                "SLZ.Marrow.Interaction.EntityTransformInfo");
            audit.RequireField(marrowBody, "_rigidbody", "UnityEngine.Rigidbody");
            audit.RequireField(marrowBody, "_colliders", "UnityEngine.Collider[]");
            audit.RequireField(marrowBody, "_trackers",
                "SLZ.Marrow.Interaction.Tracker[]");
            audit.RequireField(marrowBody, "_triggers", "UnityEngine.Collider[]");
            audit.RequireField(marrowBody, "_bodiesToIgnore",
                "SLZ.Marrow.Interaction.MarrowBody[]");
            audit.RequireField(marrowBody, "_collidersToIgnore",
                "UnityEngine.Collider[]");
            audit.RequireField(marrowBody, "<HasRigidbody>k__BackingField",
                "System.Boolean");
            audit.RequireField(marrowBody, "<isCenterOfMassOverride>k__BackingField",
                "System.Boolean");
            audit.RequireField(marrowBody, "<CenterOfMass>k__BackingField",
                "UnityEngine.Vector3");

            Type marrowJoint = audit.RequireType(
                "SLZ.Marrow.Interaction.MarrowJoint", MarrowAssembly);
            audit.RequireBaseType(marrowJoint, "UnityEngine.MonoBehaviour");
            audit.RequireField(marrowJoint, "_defaultConfigJointInfo",
                "SLZ.Marrow.Interaction.ConfigJointInfo");
            audit.RequireField(marrowJoint, "_bodyA",
                "SLZ.Marrow.Interaction.MarrowBody");
            audit.RequireField(marrowJoint, "_bodyB",
                "SLZ.Marrow.Interaction.MarrowBody");
            audit.RequireField(marrowJoint, "_configurableJoint",
                "UnityEngine.ConfigurableJoint");
            audit.RequireField(marrowJoint, "_entity",
                "SLZ.Marrow.Interaction.MarrowEntity");

            Type marrowEntity = audit.RequireType(
                "SLZ.Marrow.Interaction.MarrowEntity", MarrowAssembly);
            audit.RequireBaseType(marrowEntity, "UnityEngine.MonoBehaviour");
            audit.RequireField(marrowEntity, "_bodies",
                "SLZ.Marrow.Interaction.MarrowBody[]");
            audit.RequireField(marrowEntity, "_joints",
                "SLZ.Marrow.Interaction.MarrowJoint[]");
            audit.RequireField(marrowEntity, "_anchorBody",
                "SLZ.Marrow.Interaction.MarrowBody");
            audit.RequireField(marrowEntity, "_poolee", "SLZ.Marrow.Pool.Poolee");
            audit.RequireField(marrowEntity, "_behaviours",
                "SLZ.Marrow.Interaction.MarrowBehaviour[]");
            audit.RequireField(marrowEntity, "_originalScale", "UnityEngine.Vector3");

            Type rigidbodyInfo = audit.RequireType(
                "SLZ.Marrow.Interaction.RigidbodyInfo", MarrowAssembly);
            audit.RequireField(rigidbodyInfo, "mass", "System.Single");
            audit.RequireField(rigidbodyInfo, "drag", "System.Single");
            audit.RequireField(rigidbodyInfo, "angularDrag", "System.Single");
            audit.RequireField(rigidbodyInfo, "useGravity", "System.Boolean");
            audit.RequireField(rigidbodyInfo, "isKinematic", "System.Boolean");
            audit.RequireField(rigidbodyInfo, "detectCollisions", "System.Boolean");
            audit.RequireField(rigidbodyInfo, "interpolate", "System.Boolean");
            audit.RequireField(rigidbodyInfo, "collisionDetection", "System.Int32");
            audit.RequireField(rigidbodyInfo, "constraints", "System.Int32");
            audit.RequireField(rigidbodyInfo, "centerOfMass", "UnityEngine.Vector3");
            audit.RequireField(rigidbodyInfo, "inertiaTensor", "UnityEngine.Vector3");
            audit.RequireField(rigidbodyInfo, "inertiaTensorRotation",
                "UnityEngine.Quaternion");
            audit.RequireField(rigidbodyInfo, "initalVelocity", "UnityEngine.Vector3");
            audit.RequireField(rigidbodyInfo, "initialAngularVelocity",
                "UnityEngine.Vector3");

            Type entityTransformInfo = audit.RequireType(
                "SLZ.Marrow.Interaction.EntityTransformInfo", MarrowAssembly);
            audit.RequireField(entityTransformInfo, "position", "UnityEngine.Vector3");
            audit.RequireField(entityTransformInfo, "rotation",
                "UnityEngine.Quaternion");

            Type jointInfo = audit.RequireType(
                "SLZ.Marrow.Interaction.ConfigJointInfo", MarrowAssembly);
            audit.RequireField(jointInfo, "startRotation", "UnityEngine.Quaternion");
            audit.RequireField(jointInfo, "axis", "UnityEngine.Vector3");
            audit.RequireField(jointInfo, "secondaryAxis", "UnityEngine.Vector3");
            audit.RequireField(jointInfo, "anchor", "UnityEngine.Vector3");
            audit.RequireField(jointInfo, "connectedAnchor", "UnityEngine.Vector3");
            audit.RequireField(jointInfo, "autoConfigureConnectedAnchor",
                "System.Boolean");
            audit.RequireField(jointInfo, "breakForce", "System.Single");
            audit.RequireField(jointInfo, "breakTorque", "System.Single");
            audit.RequireField(jointInfo, "enableCollision", "System.Boolean");
            audit.RequireField(jointInfo, "enablePreprocessing", "System.Boolean");
            audit.RequireField(jointInfo, "massScale", "System.Single");
            audit.RequireField(jointInfo, "connectedMassScale", "System.Single");
            audit.RequireField(jointInfo, "projectionAngle", "System.Single");
            audit.RequireField(jointInfo, "projectionDistance", "System.Single");
            audit.RequireField(jointInfo, "projectionModeExt", "System.Int32");
            audit.RequireField(jointInfo, "slerpDriveExt",
                "SLZ.Marrow.Interaction.JointDriveExt");
            audit.RequireField(jointInfo, "angularYZDriveExt",
                "SLZ.Marrow.Interaction.JointDriveExt");
            audit.RequireField(jointInfo, "angularXDriveExt",
                "SLZ.Marrow.Interaction.JointDriveExt");
            audit.RequireField(jointInfo, "rotationDriveMode", "System.Int32");
            audit.RequireField(jointInfo, "targetAngularVelocity",
                "UnityEngine.Vector3");
            audit.RequireField(jointInfo, "targetRotation", "UnityEngine.Quaternion");
            audit.RequireField(jointInfo, "zDriveExt",
                "SLZ.Marrow.Interaction.JointDriveExt");
            audit.RequireField(jointInfo, "yDriveExt",
                "SLZ.Marrow.Interaction.JointDriveExt");
            audit.RequireField(jointInfo, "xDriveExt",
                "SLZ.Marrow.Interaction.JointDriveExt");
            audit.RequireField(jointInfo, "targetVelocity", "UnityEngine.Vector3");
            audit.RequireField(jointInfo, "targetPosition", "UnityEngine.Vector3");
            foreach (string field in new[]
                     {
                         "angularZLimitExt", "angularYLimitExt",
                         "highAngularXLimitExt", "lowAngularXLimitExt",
                         "linearLimitExt",
                     })
                audit.RequireField(jointInfo, field,
                    "SLZ.Marrow.Interaction.SoftJointLimitExt");
            foreach (string field in new[]
                     {
                         "angularYZLimitSpringExt", "angularXLimitSpringExt",
                         "linearLimitSpringExt",
                     })
                audit.RequireField(jointInfo, field,
                    "SLZ.Marrow.Interaction.SoftJointLimitSpringExt");
            audit.RequireField(jointInfo, "angularXMotion", "System.Int32");
            audit.RequireField(jointInfo, "angularYMotion", "System.Int32");
            audit.RequireField(jointInfo, "angularZMotion", "System.Int32");
            audit.RequireField(jointInfo, "xMotion", "System.Int32");
            audit.RequireField(jointInfo, "yMotion", "System.Int32");
            audit.RequireField(jointInfo, "zMotion", "System.Int32");
            audit.RequireField(jointInfo, "configuredInWorldSpace", "System.Boolean");
            audit.RequireField(jointInfo, "swapBodies", "System.Boolean");

            Type jointDrive = audit.RequireType(
                "SLZ.Marrow.Interaction.JointDriveExt", MarrowAssembly);
            audit.RequireField(jointDrive, "positionSpring", "System.Single");
            audit.RequireField(jointDrive, "positionDamper", "System.Single");
            audit.RequireField(jointDrive, "maximumForce", "System.Single");
            Type jointLimit = audit.RequireType(
                "SLZ.Marrow.Interaction.SoftJointLimitExt", MarrowAssembly);
            audit.RequireField(jointLimit, "limit", "System.Single");
            audit.RequireField(jointLimit, "bounciness", "System.Single");
            audit.RequireField(jointLimit, "contactDistance", "System.Single");
            Type jointLimitSpring = audit.RequireType(
                "SLZ.Marrow.Interaction.SoftJointLimitSpringExt", MarrowAssembly);
            audit.RequireField(jointLimitSpring, "spring", "System.Single");
            audit.RequireField(jointLimitSpring, "damper", "System.Single");

            Type trackerSettings = audit.RequireType(
                "SLZ.Marrow.Interaction.TrackerSettings", MarrowAssembly);
            audit.RequireField(trackerSettings, "layers", "System.Int32");
            audit.RequireField(trackerSettings, "settings",
                "SLZ.Marrow.Interaction.TrackerSetting[]");
            Type trackerSetting = audit.RequireType(
                "SLZ.Marrow.Interaction.TrackerSetting", MarrowAssembly);
            audit.RequireField(trackerSetting, "isActive", "System.Boolean");
            audit.RequireField(trackerSetting, "layer", "System.Int32");
            audit.RequireField(trackerSetting, "type", "System.Int32");
            audit.RequireField(trackerSetting, "center", "UnityEngine.Vector3");
            audit.RequireField(trackerSetting, "size", "UnityEngine.Vector3");
            audit.RequireField(trackerSetting, "radius", "System.Single");
            audit.RequireField(trackerSetting, "height", "System.Single");
            audit.RequireField(trackerSetting, "direction", "System.Int32");

            Type puppetMaster = audit.RequireType(
                "SLZ.Marrow.PuppetMasta.PuppetMaster", MarrowAssembly);
            audit.RequireBaseType(
                puppetMaster, "SLZ.Marrow.Interaction.MarrowBehaviour");
            audit.RequireField(puppetMaster, "marrowEntity",
                "SLZ.Marrow.Interaction.MarrowEntity");
            audit.RequireField(puppetMaster, "_poolee", "SLZ.Marrow.Pool.Poolee");
            audit.RequireField(puppetMaster, "humanoidConfig",
                "SLZ.Marrow.PuppetMasta.PuppetMasterHumanoidConfig");
            audit.RequireField(puppetMaster, "targetRoot", "UnityEngine.Transform");
            audit.RequireField(puppetMaster, "state", "System.Int32");
            audit.RequireField(puppetMaster, "stateSettings",
                "SLZ.Marrow.PuppetMasta.StateSettings");
            audit.RequireField(puppetMaster, "mode", "System.Int32");
            audit.RequireField(puppetMaster, "blendTime", "System.Single");
            audit.RequireField(puppetMaster, "solverIterationCount", "System.Int32");
            audit.RequireField(puppetMaster, "visualizeTargetAnimation", "System.Byte");
            audit.RequireField(puppetMaster, "visualizeTargetPose", "System.Byte");
            audit.RequireField(puppetMaster, "mappingWeight", "System.Single");
            audit.RequireField(puppetMaster, "muscleWeight", "System.Single");
            audit.RequireField(puppetMaster, "muscleSpring", "System.Single");
            audit.RequireField(puppetMaster, "muscleDamper", "System.Single");
            audit.RequireField(puppetMaster, "updateJointAnchors", "System.Byte");
            audit.RequireField(puppetMaster, "angularLimits", "System.Byte");
            audit.RequireField(puppetMaster, "internalCollisions", "System.Byte");
            audit.RequireField(puppetMaster, "muscles",
                "SLZ.Marrow.PuppetMasta.Muscle[]");
            audit.RequireField(puppetMaster, "cullAnimators", "System.Byte");
            audit.RequireField(puppetMaster, "cullableAnimators",
                "UnityEngine.Animator[]");
            audit.RequireField(puppetMaster, "solvers",
                "SLZ.Marrow.PuppetMasta.SolverManager[]");

            Type stateSettings = audit.RequireType(
                "SLZ.Marrow.PuppetMasta.StateSettings", MarrowAssembly);
            audit.RequireValueType(stateSettings);
            audit.RequireField(stateSettings, "killDuration", "System.Single");
            audit.RequireField(stateSettings, "deadMuscleWeight", "System.Single");
            audit.RequireField(stateSettings, "deadMuscleDamper", "System.Single");
            audit.RequireField(stateSettings, "maxFreezeSqrVelocity", "System.Single");
            audit.RequireField(stateSettings, "enableAngularLimitsOnKill", "System.Byte");
            audit.RequireField(stateSettings, "enableInternalCollisionsOnKill",
                "System.Byte");

            Type muscle = audit.RequireType(
                "SLZ.Marrow.PuppetMasta.Muscle", MarrowAssembly);
            audit.RequireValueType(muscle);
            audit.RequireField(muscle, "name", "System.String");
            audit.RequireField(muscle, "target", "UnityEngine.Transform");
            audit.RequireField(muscle, "props",
                "SLZ.Marrow.PuppetMasta.Muscle+Props");
            audit.RequireField(muscle, "parentIndexes", "System.Int32[]");
            audit.RequireField(muscle, "childIndexes", "System.Int32[]");
            audit.RequireField(muscle, "childFlags", "System.Byte[]");
            audit.RequireField(muscle, "kinshipDegrees", "System.Int32[]");
            audit.RequireField(muscle, "broadcaster",
                "SLZ.Marrow.PuppetMasta.MuscleCollisionBroadcasterSensor");
            audit.RequireField(muscle, "jointBreakBroadcaster", "UnityEngine.Object");
            audit.RequireField(muscle, "positionOffset", "UnityEngine.Vector3");
            audit.RequireField(muscle, "mappedVelocity", "UnityEngine.Vector3");
            audit.RequireField(muscle, "mappedAngularVelocity", "UnityEngine.Vector3");
            audit.RequireField(muscle, "marrowJoint",
                "SLZ.Marrow.Interaction.MarrowJoint");
            audit.RequireField(muscle, "marrowBody",
                "SLZ.Marrow.Interaction.MarrowBody");

            Type muscleProps = audit.RequireType(
                "SLZ.Marrow.PuppetMasta.Muscle+Props", MarrowAssembly);
            audit.RequireValueType(muscleProps);
            audit.RequireField(muscleProps, "group", "System.Int32");
            audit.RequireField(muscleProps, "mappingWeight", "System.Single");
            audit.RequireField(muscleProps, "muscleWeight", "System.Single");
            audit.RequireField(muscleProps, "muscleDamper", "System.Single");
            audit.RequireField(muscleProps, "mapPosition", "System.Byte");
            audit.RequireField(muscleProps, "ignoredMuscleIndexs", "System.Int32[]");
            return audit;
        }

        private static CapabilityAudit ProbeAi()
        {
            var audit = new CapabilityAudit(NpcCompatibilityCapabilities.AI, "AI");

            Type behaviourBase = audit.RequireType(
                "SLZ.Marrow.PuppetMasta.BehaviourBaseNav", MarrowAssembly);
            audit.RequireBaseType(behaviourBase, "UnityEngine.MonoBehaviour");

            Type brain = audit.RequireType("SLZ.Marrow.AI.AIBrain", MarrowAssembly);
            audit.RequireBaseType(brain, "UnityEngine.MonoBehaviour");
            audit.RequireField(brain, "_poolee", "SLZ.Marrow.Pool.Poolee");
            audit.RequireField(brain, "behaviour",
                "SLZ.Marrow.PuppetMasta.BehaviourBaseNav");
            audit.RequireField(brain, "puppetMaster",
                "SLZ.Marrow.PuppetMasta.PuppetMaster");
            audit.RequireField(brain, "dontClearBaseConfig", "System.Byte");
            audit.RequireField(brain, "isDead", "System.Byte");

            Type powerLegs = audit.RequireType(
                "PuppetMasta.BehaviourPowerLegs", GameAssembly);
            audit.RequireBaseType(
                powerLegs, "SLZ.Marrow.PuppetMasta.BehaviourBaseNav");
            audit.RequireField(powerLegs, "puppetMaster",
                "SLZ.Marrow.PuppetMasta.PuppetMaster");
            audit.RequireField(powerLegs, "_poolee", "SLZ.Marrow.Pool.Poolee");
            audit.RequireField(powerLegs, "overrideConfig",
                "SLZ.Marrow.PuppetMasta.BaseEnemyConfig");
            audit.RequireField(powerLegs, "prefabConfig",
                "SLZ.Marrow.PuppetMasta.BaseEnemyConfig");
            audit.RequireField(powerLegs, "restingRange", "System.Single");
            audit.RequireField(powerLegs, "activeRange", "System.Single");
            audit.RequireField(powerLegs, "roamSpeed", "System.Single");
            audit.RequireField(powerLegs, "roamAngSpeed", "System.Single");
            audit.RequireField(powerLegs, "agroedSpeed", "System.Single");
            audit.RequireField(powerLegs, "agroedAngSpeed", "System.Single");
            audit.RequireField(powerLegs, "engagedSpeed", "System.Single");
            audit.RequireField(powerLegs, "eyeTran", "UnityEngine.Transform");
            audit.RequireField(powerLegs, "sensors", "PuppetMasta.SubBehaviourSensors");
            audit.RequireField(powerLegs, "sfx", "PuppetMasta.SubBehaviourSfx");
            audit.RequireField(powerLegs, "health", "PuppetMasta.SubBehaviourHealth");
            audit.RequireField(powerLegs, "hostManager",
                "SLZ.Marrow.InteractableHostManager");
            audit.RequireField(powerLegs, "ik", "PuppetMasta.SubBehaviourIk");
            audit.RequireField(powerLegs, "handPoser",
                "PuppetMasta.SubBehaviourHandPose");
            audit.RequireField(powerLegs, "standingIdle", "SLZ.Marrow.Data.EnemyPoseData");
            audit.RequireField(powerLegs, "aiLocoController",
                "PuppetMasta.LocoController");
            audit.RequireField(powerLegs, "useAiLocoController", "System.Byte");
            audit.RequireField(powerLegs, "onGetUpProne", "PuppetMasta.AnimatorEvent[]");
            audit.RequireField(powerLegs, "onGetUpSupine", "PuppetMasta.AnimatorEvent[]");

            Type sensors = audit.RequireType(
                "PuppetMasta.SubBehaviourSensors", GameAssembly);
            audit.RequireField(sensors, "blockVisionRaycast", "UnityEngine.LayerMask");
            audit.RequireField(sensors, "visionFov", "System.Single");
            audit.RequireField(sensors, "forceSensorsFeet",
                "SLZ.Marrow.PuppetMasta.MuscleCollisionBroadcasterSensor[]");
            audit.RequireField(sensors, "forceSensorsHands",
                "SLZ.Marrow.PuppetMasta.MuscleCollisionBroadcasterSensor[]");
            audit.RequireField(sensors, "forceSensorsBody",
                "SLZ.Marrow.PuppetMasta.MuscleCollisionBroadcasterSensor[]");
            audit.RequireField(sensors, "additionalMass", "System.Single");
            audit.RequireField(sensors, "footSupported", "System.Single");
            audit.RequireField(sensors, "handSupported", "System.Single");
            audit.RequireField(sensors, "bodySupported", "System.Single");
            audit.RequireField(sensors, "target", "SLZ.Marrow.AI.TriggerRefProxy");
            audit.RequireField(sensors, "selfTrp", "SLZ.Marrow.AI.TriggerRefProxy");

            Type sfx = audit.RequireType("PuppetMasta.SubBehaviourSfx", GameAssembly);
            foreach (string clipsField in new[]
                     {
                         "agro", "unAgro", "painSmall", "painBig", "death",
                         "jumpCharge", "jump", "smallEffort", "mediumEffort",
                         "largeEffort", "attack1", "attackLand1", "attack2",
                         "impactHead", "impactSpine", "impactLimb",
                     })
                audit.RequireField(sfx, clipsField, "UnityEngine.AudioClip[]");
            foreach (string loopField in new[]
                     {
                         "dotLoop1", "agroMovementLoop", "movementLoop",
                     })
                audit.RequireField(sfx, loopField, "UnityEngine.AudioClip");
            audit.RequireField(sfx, "pitchMultiplier", "System.Single");
            audit.RequireField(sfx, "impactSource", "UnityEngine.AudioSource");

            Type health = audit.RequireType(
                "PuppetMasta.SubBehaviourHealth", GameAssembly);
            audit.RequireField(health, "maxHitPoints", "System.Single");
            audit.RequireField(health, "maxAppendageHp", "System.Single");
            audit.RequireField(health, "stunRecovery", "System.Single");
            audit.RequireField(health, "maxStunSeconds", "System.Single");
            audit.RequireField(health, "muscles", "System.Int32[]");
            audit.RequireField(health, "aggression", "System.Single");

            Type ik = audit.RequireType("PuppetMasta.SubBehaviourIk", GameAssembly);
            audit.RequireField(ik, "footIkOn", "System.Byte");
            audit.RequireField(ik, "footIkSolvers", "PuppetMasta.IK[]");
            audit.RequireField(ik, "armIkSolvers", "PuppetMasta.IK[]");
            audit.RequireField(ik, "toeTrans", "UnityEngine.Transform[]");
            audit.RequireField(ik, "lfHandTarget", "UnityEngine.Transform");
            audit.RequireField(ik, "rtHandTarget", "UnityEngine.Transform");
            audit.RequireField(ik, "lfHandAnim", "UnityEngine.Transform");
            audit.RequireField(ik, "rtHandAnim", "UnityEngine.Transform");
            audit.RequireField(ik, "lfShoulderMuscleIndex", "System.Int32");
            audit.RequireField(ik, "rtShoulderMuscleIndex", "System.Int32");
            audit.RequireField(ik, "isHuman", "System.Byte");

            Type animatorEvent = audit.RequireType(
                "PuppetMasta.AnimatorEvent", GameAssembly);
            audit.RequireField(animatorEvent, "animationState", "System.String");
            audit.RequireField(animatorEvent, "crossfadeTime", "System.Single");
            audit.RequireField(animatorEvent, "layer", "System.Int32");
            audit.RequireField(animatorEvent, "resetNormalizedTime", "System.Byte");

            Type handPoser = audit.RequireType(
                "PuppetMasta.SubBehaviourHandPose", GameAssembly);
            foreach (string poseField in new[]
                     {
                         "OpenHand", "Fist", "Pistol", "PistolOffhand",
                     })
                audit.RequireField(handPoser, poseField,
                    "SLZ.Marrow.Data.HandPoseData");
            audit.RequireField(handPoser, "leftHandRefs", "UnityEngine.Transform[]");
            audit.RequireField(handPoser, "rightHandRefs", "UnityEngine.Transform[]");

            Type baseConfig = audit.RequireType(
                "SLZ.Marrow.PuppetMasta.BaseEnemyConfig", MarrowAssembly);
            audit.RequireBaseType(baseConfig, "UnityEngine.ScriptableObject");
            foreach (string movementField in new[]
                     {
                         "roamSpeed", "roamAngSpeed", "agroedSpeed",
                         "agroedAngSpeed",
                     })
                audit.RequireField(baseConfig, movementField, "System.Single");
            audit.RequireField(baseConfig, "sensorSettings",
                "SLZ.Marrow.PuppetMasta.BaseEnemyConfig+SensorSettings");
            audit.RequireField(baseConfig, "healthSettings",
                "SLZ.Marrow.PuppetMasta.BaseEnemyConfig+HealthSettings");
            audit.RequireField(baseConfig, "restingUsage",
                "SLZ.Marrow.PuppetMasta.SubBehaviourHealth+UsageSettings");
            audit.RequireField(baseConfig, "agroedUsage",
                "SLZ.Marrow.PuppetMasta.SubBehaviourHealth+UsageSettings");

            Type pose = audit.RequireType("SLZ.Marrow.Data.EnemyPoseData", MarrowAssembly);
            audit.RequireBaseType(pose, "UnityEngine.ScriptableObject");
            audit.RequireField(pose, "posePositions", "UnityEngine.Vector3[]");
            audit.RequireField(pose, "poseRotations", "UnityEngine.Quaternion[]");

            Type host = audit.RequireType("SLZ.Marrow.InteractableHost", MarrowAssembly);
            audit.RequireBaseType(
                host, "SLZ.Marrow.Interaction.MarrowBehaviour");
            audit.RequireField(host, "marrowEntity",
                "SLZ.Marrow.Interaction.MarrowEntity");
            audit.RequireField(host, "manager", "SLZ.Marrow.InteractableHostManager");
            audit.RequireField(host, "ignoreBodyOnGrab", "System.Byte");
            audit.RequireField(host, "<VirtualController>k__BackingField",
                "SLZ.Marrow.Interaction.VirtualController");
            audit.RequireField(host, "<IsStatic>k__BackingField", "System.Byte");

            Type virtualController = audit.RequireType(
                "SLZ.Marrow.Interaction.VirtualController", MarrowAssembly);
            audit.RequireField(virtualController, "defaultSettings",
                "SLZ.Marrow.Interaction.VirtualControllerSettings");
            Type controllerSettings = audit.RequireType(
                "SLZ.Marrow.Interaction.VirtualControllerSettings", MarrowAssembly);
            foreach (string scalar in new[]
                     {
                         "lookRotationWeight", "handTwistWeight", "handSwingWeight",
                         "positionWeight", "jointSwingLimit", "jointTwistLimit",
                     })
                audit.RequireField(controllerSettings, scalar, "System.Single");
            audit.RequireField(controllerSettings, "autoTargetUpdatePrimary",
                "System.Boolean");
            audit.RequireField(controllerSettings, "dynamicHandDistanceWeights",
                "System.Boolean");

            Type hostManager = audit.RequireType(
                "SLZ.Marrow.InteractableHostManager", MarrowAssembly);
            audit.RequireBaseType(hostManager, "UnityEngine.MonoBehaviour");
            audit.RequireField(hostManager, "hosts", "SLZ.Marrow.InteractableHost[]");
            audit.RequireField(hostManager, "grabbedHosts",
                "SLZ.Marrow.InteractableHost[]");

            Type tracker = audit.RequireType(
                "SLZ.Marrow.Interaction.Tracker", MarrowAssembly);
            audit.RequireBaseType(tracker, "UnityEngine.MonoBehaviour");
            audit.RequireField(tracker, "_entity",
                "SLZ.Marrow.Interaction.MarrowEntity");
            audit.RequireField(tracker, "_body",
                "SLZ.Marrow.Interaction.MarrowBody");
            audit.RequireField(tracker, "_collider", "UnityEngine.Collider");

            Type entityForTags = audit.RequireType(
                "SLZ.Marrow.Interaction.MarrowEntity", MarrowAssembly);
            audit.RequireField(entityForTags, "_tags", "SLZ.Marrow.Warehouse.TagList");
            Type tagList = audit.RequireType(
                "SLZ.Marrow.Warehouse.TagList", MarrowAssembly);
            audit.RequireField(tagList, "_tags",
                "System.Collections.Generic.List`1[[SLZ.Marrow.Warehouse.BoneTagReference, "
                + "SLZ.Marrow, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null]]");
            audit.RequireType("SLZ.Marrow.Warehouse.BoneTagReference", MarrowAssembly);
            Type barcode = audit.RequireType(
                "SLZ.Marrow.Warehouse.Barcode", MarrowAssembly);
            audit.RequireField(barcode, "_id", "System.String");

            Type triggerProxy = audit.RequireType(
                "SLZ.Marrow.AI.TriggerRefProxy", MarrowAssembly);
            audit.RequireBaseType(triggerProxy, "UnityEngine.MonoBehaviour");
            audit.RequireField(triggerProxy, "triggerType", "System.Int32");
            audit.RequireField(triggerProxy, "npcType", "System.Int32");
            audit.RequireField(triggerProxy, "teamNumber", "System.Int32");
            audit.RequireField(triggerProxy, "root", "UnityEngine.GameObject");
            audit.RequireField(triggerProxy, "targetHead", "UnityEngine.Rigidbody");
            audit.RequireField(triggerProxy, "lfHandRb", "UnityEngine.Rigidbody");
            audit.RequireField(triggerProxy, "rtHandRb", "UnityEngine.Rigidbody");
            audit.RequireField(triggerProxy, "chestTran", "UnityEngine.Transform");
            audit.RequireField(triggerProxy, "feetTran", "UnityEngine.Transform");
            audit.RequireField(triggerProxy, "legacyProxy", "UnityEngine.Transform");

            Type forceSensor = audit.RequireType(
                "SLZ.Marrow.PuppetMasta.MuscleCollisionBroadcasterSensor",
                MarrowAssembly);
            audit.RequireBaseType(forceSensor, "UnityEngine.MonoBehaviour");
            audit.RequireField(forceSensor, "puppetMaster",
                "SLZ.Marrow.PuppetMasta.PuppetMaster");
            audit.RequireField(forceSensor, "muscleIndex", "System.Int32");
            audit.RequireField(forceSensor, "isGrounded", "System.Byte");
            audit.RequireField(forceSensor, "groundNormal", "UnityEngine.Vector3");
            audit.RequireField(forceSensor, "_totalImpulse", "UnityEngine.Vector3");
            audit.RequireField(forceSensor, "totalMass", "System.Single");
            audit.RequireField(forceSensor, "additionalMass", "System.Single");

            Type liteLoco = audit.RequireType(
                "SLZ.Marrow.Mechanics.LiteLoco", MarrowAssembly);
            audit.RequireBaseType(liteLoco, "UnityEngine.MonoBehaviour");
            audit.RequireField(liteLoco, "weight", "System.Single");
            audit.RequireField(liteLoco, "root", "UnityEngine.Transform");
            audit.RequireField(liteLoco, "neutralRoot", "UnityEngine.Transform");
            audit.RequireField(liteLoco, "stepGroups",
                "SLZ.Marrow.Mechanics.StepGroup[]");

            Type stepGroup = audit.RequireType(
                "SLZ.Marrow.Mechanics.StepGroup", MarrowAssembly);
            audit.RequireField(stepGroup, "pelvis", "UnityEngine.Transform");
            audit.RequireField(stepGroup, "sisterStepGroup", "System.Int32");
            audit.RequireField(stepGroup, "legLength", "System.Single");
            audit.RequireField(stepGroup, "FootXVCurve", "UnityEngine.AnimationCurve");
            audit.RequireField(stepGroup, "footsteps",
                "SLZ.Marrow.Mechanics.Footstep[]");
            audit.RequireField(stepGroup, "gears", "SLZ.Marrow.Mechanics.Gear[]");
            audit.RequireField(stepGroup, "grounder", "SLZ.Marrow.Mechanics.Grounder");

            Type gear = audit.RequireType(
                "SLZ.Marrow.Mechanics.Gear", MarrowAssembly);
            foreach (string scalar in new[]
                     {
                         "upshiftVel", "downshiftVel", "stepProgressThreshold",
                         "stepfromtoWeight", "minStepThreshold",
                     })
                audit.RequireField(gear, scalar, "System.Single");
            foreach (string curve in new[]
                     {
                         "StepRateVCurve", "stepHeight", "StepZInterp",
                         "StepAnkleBend", "MuscleUsage",
                     })
                audit.RequireField(gear, curve, "UnityEngine.AnimationCurve");

            Type grounder = audit.RequireType(
                "SLZ.Marrow.Mechanics.Grounder", MarrowAssembly);
            audit.RequireField(grounder, "layers", "UnityEngine.LayerMask");
            audit.RequireField(grounder, "maxStep", "System.Single");
            audit.RequireField(grounder, "footSpeed", "System.Single");

            Type footstep = audit.RequireType(
                "SLZ.Marrow.Mechanics.Footstep", MarrowAssembly);
            audit.RequireField(footstep, "hip", "UnityEngine.Transform");
            audit.RequireField(footstep, "foot", "UnityEngine.Transform");
            audit.RequireField(footstep, "neutralTarget", "UnityEngine.Transform");
            audit.RequireField(footstep, "footCollider", "UnityEngine.Collider");
            audit.RequireField(footstep, "liftedMat", "UnityEngine.PhysicMaterial");
            audit.RequireField(footstep, "stepSfx", "SLZ.Marrow.Audio.FootstepSFX");
            audit.RequireField(footstep, "rotationOffset", "System.Single");

            Type footstepSfx = audit.RequireType(
                "SLZ.Marrow.Audio.FootstepSFX", MarrowAssembly);
            audit.RequireBaseType(footstepSfx, "UnityEngine.MonoBehaviour");
            audit.RequireField(footstepSfx, "volumeMult", "System.Single");
            audit.RequireField(footstepSfx, "walkConcrete", "UnityEngine.AudioClip[]");
            audit.RequireField(footstepSfx, "runConcrete", "UnityEngine.AudioClip[]");

            Type rebind = audit.RequireType(
                "SLZ.Marrow.PuppetMasta.SkinnedBoneRebind", MarrowAssembly);
            audit.RequireBaseType(rebind, "UnityEngine.MonoBehaviour");
            audit.RequireField(rebind, "bones", "UnityEngine.Transform[]");
            audit.RequireField(rebind, "rebindBone", "System.Byte[]");
            audit.RequireField(rebind, "skinnedMeshRenderer",
                "UnityEngine.SkinnedMeshRenderer");
            audit.RequireField(rebind, "meshToRead", "UnityEngine.Mesh");
            audit.RequireField(rebind, "meshToWrite", "UnityEngine.Mesh");

            Type limbIk = audit.RequireType("SLZ.VRMK.LimbIKSlz", GameAssembly);
            audit.RequireBaseType(limbIk, "PuppetMasta.IK");
            audit.RequireField(limbIk, "fixTransforms", "System.Byte");
            audit.RequireField(limbIk, "animator", "UnityEngine.Animator");
            audit.RequireField(limbIk, "solver", "SLZ.VRMK.IKSolverLimbSlz");
            Type ikSolver = audit.RequireType(
                "SLZ.VRMK.IKSolverLimbSlz", GameAssembly);
            audit.RequireField(ikSolver, "IKPosition", "UnityEngine.Vector3");
            audit.RequireField(ikSolver, "IKPositionWeight", "System.Single");
            audit.RequireField(ikSolver, "root", "UnityEngine.Transform");
            audit.RequireField(ikSolver, "target", "UnityEngine.Transform");
            audit.RequireField(ikSolver, "IKRotation", "UnityEngine.Quaternion");
            audit.RequireField(ikSolver, "IKRotationWeight", "System.Single");
            audit.RequireField(ikSolver, "bendNormal", "UnityEngine.Vector3");
            audit.RequireField(ikSolver, "bendGoal", "UnityEngine.Transform");
            audit.RequireField(ikSolver, "bone1", "SLZ.VRMK.TrigonometricBone");
            audit.RequireField(ikSolver, "bone2", "SLZ.VRMK.TrigonometricBone");
            audit.RequireField(ikSolver, "bone3", "SLZ.VRMK.TrigonometricBone");
            Type trigBone = audit.RequireType(
                "SLZ.VRMK.TrigonometricBone", GameAssembly);
            audit.RequireField(trigBone, "transform", "UnityEngine.Transform");
            audit.RequireField(trigBone, "solverPosition", "UnityEngine.Vector3");
            audit.RequireField(trigBone, "solverRotation", "UnityEngine.Quaternion");
            audit.RequireField(trigBone, "defaultLocalPosition", "UnityEngine.Vector3");
            audit.RequireField(trigBone, "defaultLocalRotation", "UnityEngine.Quaternion");
            audit.RequireField(trigBone, "length", "System.Single");
            audit.RequireField(trigBone, "sqrMag", "System.Single");
            audit.RequireField(trigBone, "axis", "UnityEngine.Vector3");

            Type visualDamage = audit.RequireType(
                "SLZ.Marrow.Combat.VisualDamageController", MarrowAssembly);
            audit.RequireBaseType(visualDamage, "UnityEngine.MonoBehaviour");
            audit.RequireField(visualDamage, "Renderers", "UnityEngine.Renderer[]");
            audit.RequireField(visualDamage, "meshScaleFactor", "System.Single");
            audit.RequireField(visualDamage, "hitScaleFactor", "System.Single");

            Type impactProperties = audit.RequireType(
                "SLZ.Marrow.ImpactProperties", MarrowAssembly);
            audit.RequireBaseType(impactProperties, "UnityEngine.MonoBehaviour");
            audit.RequireField(
                impactProperties,
                "decalType",
                "SLZ.Marrow.ImpactProperties+DecalType");

            Type damageReceiver = audit.RequireType(
                "SLZ.Combat.VisualDamageReceiver", GameAssembly);
            audit.RequireBaseType(damageReceiver, "UnityEngine.MonoBehaviour");
            audit.RequireField(damageReceiver, "orgpos", "UnityEngine.Vector3");
            audit.RequireField(damageReceiver, "orgrot", "UnityEngine.Quaternion");
            audit.RequireField(damageReceiver, "orgScale", "UnityEngine.Vector3");
            audit.RequireField(damageReceiver, "bone", "UnityEngine.Transform");
            audit.RequireField(
                damageReceiver,
                "visualDamageController",
                "SLZ.Marrow.Combat.VisualDamageController");

            Type navAgent = audit.RequireType(
                "UnityEngine.AI.NavMeshAgent", "UnityEngine.AIModule");
            audit.RequireBaseType(navAgent, "UnityEngine.Behaviour");

            Type agentLink = audit.RequireType(
                "SLZ.Bonelab.AgentLinkControl", GameAssembly);
            audit.RequireBaseType(agentLink, "UnityEngine.MonoBehaviour");
            audit.RequireField(agentLink, "totalMass", "System.Single");
            audit.RequireField(agentLink, "jointForceMult", "System.Single");
            audit.RequireField(agentLink, "minLinkDupeDuration", "System.Single");
            audit.RequireField(agentLink, "distTimer", "System.Single");
            audit.RequireField(agentLink, "navAgent", "UnityEngine.AI.NavMeshAgent");
            audit.RequireField(agentLink, "brain", "SLZ.Marrow.AI.AIBrain");
            audit.RequireField(agentLink, "triggerProxy",
                "SLZ.Marrow.AI.TriggerRefProxy");
            audit.RequireField(agentLink, "baseBehaviour",
                "SLZ.Marrow.PuppetMasta.BehaviourBaseNav");
            audit.RequireField(agentLink, "legBehaviour",
                "PuppetMasta.BehaviourPowerLegs");
            audit.RequireField(agentLink, "_puppet",
                "SLZ.Marrow.PuppetMasta.PuppetMaster");
            foreach (string rigidbodyField in new[]
                     {
                         "headRB", "chestRB", "leftHandRB", "leftElbowRB",
                         "rightHandRB", "rightElbowRB", "leftFootRB", "leftKneeRB",
                         "rightFootRB", "rightKneeRB",
                     })
                audit.RequireField(agentLink, rigidbodyField, "UnityEngine.Rigidbody");
            audit.RequireField(agentLink, "allRBs", "UnityEngine.Rigidbody[]");
            foreach (string jointField in new[]
                     {
                         "headJoint", "chestJoint", "leftElbowJoint",
                         "rightElbowJoint", "leftHandJoint", "rightHandJoint",
                         "leftKneeJoint", "rightKneeJoint", "leftFootJoint",
                         "rightFootJoint",
                     })
                audit.RequireField(agentLink, jointField,
                    "UnityEngine.ConfigurableJoint");

            audit.RequireType("SLZ.Marrow.Data.HandPoseData", MarrowAssembly);
            return audit;
        }

        private static CapabilityAudit ProbePooling()
        {
            var audit = new CapabilityAudit(
                NpcCompatibilityCapabilities.Pooling, "Pooling");
            Type poolee = audit.RequireType("SLZ.Marrow.Pool.Poolee", MarrowAssembly);
            audit.RequireBaseType(poolee, "UnityEngine.MonoBehaviour");

            Type entity = audit.RequireType(
                "SLZ.Marrow.Interaction.MarrowEntity", MarrowAssembly);
            audit.RequireField(entity, "_poolee", "SLZ.Marrow.Pool.Poolee");
            audit.RequireField(entity, "_behaviours",
                "SLZ.Marrow.Interaction.MarrowBehaviour[]");

            Type body = audit.RequireType(
                "SLZ.Marrow.Interaction.MarrowBody", MarrowAssembly);
            audit.RequireField(body, "_defaultRigidbodyInfo",
                "SLZ.Marrow.Interaction.RigidbodyInfo");
            audit.RequireField(body, "<InitInEntityTransform>k__BackingField",
                "SLZ.Marrow.Interaction.EntityTransformInfo");

            Type rigidbodyInfo = audit.RequireType(
                "SLZ.Marrow.Interaction.RigidbodyInfo", MarrowAssembly);
            foreach (string scalar in new[]
                     {
                         "mass", "drag", "angularDrag",
                     })
                audit.RequireField(rigidbodyInfo, scalar, "System.Single");
            foreach (string flag in new[]
                     {
                         "useGravity", "isKinematic", "detectCollisions", "interpolate",
                     })
                audit.RequireField(rigidbodyInfo, flag, "System.Boolean");
            audit.RequireField(rigidbodyInfo, "collisionDetection", "System.Int32");
            audit.RequireField(rigidbodyInfo, "constraints", "System.Int32");
            audit.RequireField(rigidbodyInfo, "centerOfMass", "UnityEngine.Vector3");
            audit.RequireField(rigidbodyInfo, "inertiaTensor", "UnityEngine.Vector3");
            audit.RequireField(rigidbodyInfo, "inertiaTensorRotation",
                "UnityEngine.Quaternion");
            audit.RequireField(rigidbodyInfo, "initalVelocity", "UnityEngine.Vector3");
            audit.RequireField(
                rigidbodyInfo, "initialAngularVelocity", "UnityEngine.Vector3");

            Type entityTransform = audit.RequireType(
                "SLZ.Marrow.Interaction.EntityTransformInfo", MarrowAssembly);
            audit.RequireField(entityTransform, "position", "UnityEngine.Vector3");
            audit.RequireField(entityTransform, "rotation", "UnityEngine.Quaternion");

            Type puppet = audit.RequireType(
                "SLZ.Marrow.PuppetMasta.PuppetMaster", MarrowAssembly);
            audit.RequireField(puppet, "_poolee", "SLZ.Marrow.Pool.Poolee");

            Type brain = audit.RequireType("SLZ.Marrow.AI.AIBrain", MarrowAssembly);
            audit.RequireField(brain, "_poolee", "SLZ.Marrow.Pool.Poolee");

            Type powerLegs = audit.RequireType(
                "PuppetMasta.BehaviourPowerLegs", GameAssembly);
            audit.RequireField(powerLegs, "_poolee", "SLZ.Marrow.Pool.Poolee");
            return audit;
        }

        private static CapabilityAudit ProbeGrips()
        {
            var audit = new CapabilityAudit(
                NpcCompatibilityCapabilities.Grips, "Grips and hand poses");

            Type genericGrip = audit.RequireType("SLZ.Marrow.GenericGrip", MarrowAssembly);
            audit.RequireBaseType(genericGrip, "UnityEngine.MonoBehaviour");
            ProbeGripFields(audit, genericGrip);

            Type cylinderGrip = audit.RequireType(
                "SLZ.Marrow.CylinderGrip", MarrowAssembly);
            audit.RequireBaseType(cylinderGrip, "UnityEngine.MonoBehaviour");
            ProbeGripFields(audit, cylinderGrip);
            audit.RequireField(cylinderGrip, "rotationLimit", "System.Single");
            audit.RequireField(cylinderGrip, "rotationPriorityBuffer", "System.Single");
            audit.RequireField(cylinderGrip, "handPoseOnFlippedPrimaryAxis",
                "SLZ.Marrow.HandPose");
            audit.RequireField(cylinderGrip, "targetFlipOnPrimaryAxis", "System.Byte");
            audit.RequireField(cylinderGrip, "targetFlipOnTertiaryAxis", "System.Byte");
            audit.RequireField(cylinderGrip, "dynamicFriction", "System.Single");
            audit.RequireField(cylinderGrip, "staticFriction", "System.Single");
            audit.RequireField(cylinderGrip, "limit", "System.Single");
            audit.RequireField(cylinderGrip, "hasCapA", "System.Byte");
            audit.RequireField(cylinderGrip, "hasCapB", "System.Byte");
            audit.RequireField(cylinderGrip, "ignoreFlipOnZ", "System.Byte");
            audit.RequireField(cylinderGrip, "rotationalFrictionMult", "System.Single");
            audit.RequireField(cylinderGrip, "aspectRatio", "System.Single");
            audit.RequireField(cylinderGrip, "variableRadius", "System.Byte");
            audit.RequireField(cylinderGrip, "RadiusCurve", "UnityEngine.AnimationCurve");

            Type handPose = audit.RequireType("SLZ.Marrow.HandPose", MarrowAssembly);
            audit.RequireBaseType(handPose, "UnityEngine.ScriptableObject");
            return audit;
        }

        private static void ProbeGripFields(CapabilityAudit audit, Type grip)
        {
            audit.RequireField(grip, "gripColliders", "UnityEngine.Collider[]");
            audit.RequireField(grip, "additionalGripColliders", "UnityEngine.Collider[]");
            audit.RequireField(grip, "isThrowable", "System.Byte");
            audit.RequireField(grip, "ignoreGripTargetOnAttach", "System.Byte");
            audit.RequireField(grip, "handleAmplifyCurve", "UnityEngine.AnimationCurve");
            audit.RequireField(grip, "handPose", "SLZ.Marrow.HandPose");
            audit.RequireField(grip, "primaryMovementAxis", "UnityEngine.Vector3");
            audit.RequireField(grip, "secondaryMovementAxis", "UnityEngine.Vector3");
            audit.RequireFieldExists(grip, "gripOptions");
            audit.RequireField(grip, "priority", "System.Single");
            audit.RequireField(grip, "minBreakForce", "System.Single");
            audit.RequireField(grip, "maxBreakForce", "System.Single");
            audit.RequireField(grip, "defaultGripDistance", "System.Single");
            audit.RequireField(grip, "gripDistanceSqr", "System.Single");
            audit.RequireField(grip, "radius", "System.Single");
            audit.RequireField(grip, "targetTransform", "UnityEngine.Transform");
        }

        private static CapabilityAudit ProbeGaze()
        {
            var audit = new CapabilityAudit(NpcCompatibilityCapabilities.Gaze, "Gaze");

            Type animator = audit.RequireType(
                "RealisticEyeMovements.EyeAndHeadAnimator", GameAssembly);
            audit.RequireBaseType(animator, "UnityEngine.MonoBehaviour");
            audit.RequireField(animator, "headWeight", "System.Single");
            audit.RequireField(animator, "headBoneNonMecanimXform",
                "UnityEngine.Transform");
            audit.RequireField(animator, "areUpdatedControlledExternally", "System.Byte");
            audit.RequireField(animator, "eyelidsFollowEyesVertically", "System.Byte");
            audit.RequireField(animator, "controlData",
                "RealisticEyeMovements.ControlData");

            Type control = audit.RequireType(
                "RealisticEyeMovements.ControlData", GameAssembly);
            audit.RequireField(control, "eyeControl", "System.Int32");
            audit.RequireField(control, "leftEye", "UnityEngine.Transform");
            audit.RequireField(control, "rightEye", "UnityEngine.Transform");
            audit.RequireField(control, "eyelidControl", "System.Int32");
            audit.RequireField(control, "eyelidsFollowEyesVertically", "System.Byte");
            foreach (string field in new[]
                     {
                         "isEyeBallDefaultSet", "isEyeBoneDefaultSet",
                         "isEyeBallLookUpSet", "isEyeBoneLookUpSet",
                         "isEyeBallLookDownSet", "isEyeBoneLookDownSet",
                     })
                audit.RequireField(control, field, "System.Byte");
            foreach (string field in new[]
                     {
                         "upperEyeLidLeft", "upperEyeLidRight",
                         "lowerEyeLidLeft", "lowerEyeLidRight",
                     })
                audit.RequireField(control, field, "UnityEngine.Transform");
            foreach (string field in new[]
                     {
                         "leftBoneEyeRotationLimiter",
                         "rightBoneEyeRotationLimiter",
                         "leftEyeballEyeRotationLimiter",
                         "rightEyeballEyeRotationLimiter",
                     })
                audit.RequireField(
                    control, field, "RealisticEyeMovements.EyeRotationLimiter");
            audit.RequireField(
                control,
                "blendshapesForBlinking",
                "RealisticEyeMovements.EyelidPositionBlendshape[]");
            audit.RequireField(
                control,
                "blendshapesForLookingUp",
                "RealisticEyeMovements.EyelidPositionBlendshape[]");
            audit.RequireField(
                control,
                "blendshapesForLookingDown",
                "RealisticEyeMovements.EyelidPositionBlendshape[]");
            audit.RequireField(
                control,
                "blendshapesConfigs",
                "RealisticEyeMovements.BlendshapesConfig[]");

            Type limiter = audit.RequireType(
                "RealisticEyeMovements.EyeRotationLimiter", GameAssembly);
            audit.RequireField(limiter, "transform", "UnityEngine.Transform");
            audit.RequireField(limiter, "defaultQ", "UnityEngine.Quaternion");
            audit.RequireField(limiter, "lookUpQ", "UnityEngine.Quaternion");
            audit.RequireField(limiter, "lookDownQ", "UnityEngine.Quaternion");
            audit.RequireField(limiter, "maxUpAngle", "System.Single");
            audit.RequireField(limiter, "maxDownAngle", "System.Single");
            audit.RequireField(limiter, "isLookUpSet", "System.Byte");
            audit.RequireField(limiter, "isLookDownSet", "System.Byte");

            Type target = audit.RequireType(
                "RealisticEyeMovements.LookTargetController", GameAssembly);
            audit.RequireBaseType(target, "UnityEngine.MonoBehaviour");
            audit.RequireField(target, "pointsOfInterest", "UnityEngine.Transform[]");
            audit.RequireField(target, "lookAtPlayerRatio", "System.Single");
            audit.RequireField(target, "stareBackFactor", "System.Single");
            audit.RequireField(target, "noticePlayerDistance", "System.Single");
            audit.RequireField(target, "personalSpaceDistance", "System.Single");
            audit.RequireField(target, "minLookTime", "System.Single");
            audit.RequireField(target, "maxLookTime", "System.Single");
            audit.RequireField(target, "thirdPersonPlayerEyeCenter",
                "UnityEngine.Transform");
            audit.RequireField(target, "keepTargetEvenWhenLost", "System.Byte");
            foreach (string field in new[]
                     {
                         "OnStartLookingAtPlayer", "OnStopLookingAtPlayer",
                         "OnPlayerEntersPersonalSpace", "OnLookAwayFromShyness",
                     })
                audit.RequireField(
                    target, field, "UnityEngine.Events.UnityEvent");

            Type powerLegs = audit.RequireType(
                "PuppetMasta.BehaviourPowerLegs", GameAssembly);
            audit.RequireField(
                powerLegs, "OnDeathStart", "UnityEngine.Events.UnityEvent");
            return audit;
        }

        private static CapabilityAudit ProbeJawAndFace()
        {
            var audit = new CapabilityAudit(
                NpcCompatibilityCapabilities.Jaw, "Physical Jaw");

            Type powerLegs = audit.RequireType(
                "PuppetMasta.BehaviourPowerLegs", GameAssembly);
            audit.RequireField(powerLegs, "faceAnim", "PuppetMasta.SubBehaviourFaceanim");

            Type face = audit.RequireType(
                "PuppetMasta.SubBehaviourFaceanim", GameAssembly);
            audit.RequireField(face, "faceAnimEnabled", "System.Byte");
            audit.RequireField(face, "mouthTran", "UnityEngine.Transform");
            Type genericGrip = audit.RequireType(
                "SLZ.Marrow.GenericGrip", MarrowAssembly);
            audit.RequireBaseType(genericGrip, "UnityEngine.MonoBehaviour");
            ProbeGripFields(audit, genericGrip);
            Type handPose = audit.RequireType("SLZ.Marrow.HandPose", MarrowAssembly);
            audit.RequireBaseType(handPose, "UnityEngine.ScriptableObject");
            Type pose = audit.RequireType(
                "SLZ.Marrow.Data.EnemyPoseData", MarrowAssembly);
            audit.RequireBaseType(pose, "UnityEngine.ScriptableObject");
            audit.RequireField(pose, "posePositions", "UnityEngine.Vector3[]");
            audit.RequireField(pose, "poseRotations", "UnityEngine.Quaternion[]");
            return audit;
        }

        private static CapabilityAudit ProbeAudio()
        {
            var audit = new CapabilityAudit(NpcCompatibilityCapabilities.Audio, "Audio");

            Type powerLegs = audit.RequireType(
                "PuppetMasta.BehaviourPowerLegs", GameAssembly);
            audit.RequireField(powerLegs, "sfx", "PuppetMasta.SubBehaviourSfx");

            Type sfx = audit.RequireType("PuppetMasta.SubBehaviourSfx", GameAssembly);
            foreach (string clipsField in new[]
            {
                "agro", "unAgro", "painSmall", "painBig", "death", "jumpCharge",
                "jump", "smallEffort", "mediumEffort", "largeEffort", "attack1",
                "attackLand1", "attack2", "impactHead", "impactSpine", "impactLimb",
            })
            {
                audit.RequireField(sfx, clipsField, "UnityEngine.AudioClip[]");
            }
            audit.RequireField(sfx, "dotLoop1", "UnityEngine.AudioClip");
            audit.RequireField(sfx, "agroMovementLoop", "UnityEngine.AudioClip");
            audit.RequireField(sfx, "movementLoop", "UnityEngine.AudioClip");
            audit.RequireField(sfx, "pitchMultiplier", "System.Single");
            audit.RequireField(sfx, "impactSource", "UnityEngine.AudioSource");

            Type footsteps = audit.RequireType(
                "SLZ.Marrow.Audio.FootstepSFX", MarrowAssembly);
            audit.RequireBaseType(footsteps, "UnityEngine.MonoBehaviour");
            audit.RequireField(footsteps, "volumeMult", "System.Single");
            audit.RequireField(footsteps, "walkConcrete", "UnityEngine.AudioClip[]");
            audit.RequireField(footsteps, "runConcrete", "UnityEngine.AudioClip[]");
            return audit;
        }

        private static string FormatDetail(
            IEnumerable<CapabilityAudit> audits,
            bool coreBuilderAvailable,
            bool behaviourBuilderAvailable,
            string behaviourProfileDetail,
            bool gripBuilderAvailable,
            string gripProfileDetail,
            bool gazeBuilderAvailable,
            string gazeProfileDetail,
            bool jawBuilderAvailable,
            string jawProfileDetail,
            bool audioBuilderAvailable,
            string audioProfileDetail)
        {
            CapabilityAudit[] snapshot = audits.ToArray();
            string foundBuildingBlocks = string.Join(", ", snapshot
                .Where(value => value.IsAvailable)
                .Select(value => value.Label));
            string missingBuildingBlocks = string.Join(", ", snapshot
                .Where(value => !value.IsAvailable)
                .Select(value => value.Label));

            var detail = new StringBuilder();
            detail.Append(!coreBuilderAvailable
                    ? "Native core anatomy generation is unavailable. "
                    : behaviourBuilderAvailable
                        ? "Native generation coverage: Core anatomy, AI, pooling"
                          + (gripBuilderAvailable
                              ? ", player body grabs"
                              : string.Empty)
                          + (gazeBuilderAvailable
                              ? ", gaze"
                              : string.Empty)
                          + (jawBuilderAvailable
                              ? ", Physical Jaw"
                              : string.Empty)
                          + (audioBuilderAvailable
                              ? ", profile audio"
                              : string.Empty)
                          + ", and breast Secondary Motion. "
                          + (gripBuilderAvailable
                              ? string.Empty
                              : "Player body grabs remain unavailable until "
                                + "their declarations and two explicit HandPose "
                                + "assets pass. ")
                          + (gazeBuilderAvailable
                              ? string.Empty
                              : "Gaze remains unavailable until its exact "
                                + "declarations, two renderer-used template eyes, "
                                + "and explicit controller initializer pass. ")
                          + (jawBuilderAvailable
                              ? string.Empty
                              : "Physical Jaw remains unavailable until its exact "
                                + "declarations and explicit 17-entry standing pose "
                                + "pass. ")
                          + (audioBuilderAvailable
                              ? string.Empty
                              : "Profile audio remains unavailable until its "
                                + "PowerLegs and FootstepSFX declarations pass. ")
                          + "AI and pooling are one coupled build. "
                        : "Native generation coverage: Core anatomy only. "
                          + "AI and pooling remain unavailable until the exact "
                          + "Patch 6 declarations and explicit project-local "
                          + "behaviour profile both pass. ")
                .Append("Read-only BONELAB Patch 6 compatibility check: expected "
                    + "game-side building blocks found for ")
                .Append(string.IsNullOrEmpty(foundBuildingBlocks)
                    ? "none"
                    : foundBuildingBlocks)
                .Append(". Missing game-side building blocks: ")
                .Append(string.IsNullOrEmpty(missingBuildingBlocks)
                    ? "none"
                    : missingBuildingBlocks)
                .Append(". Finding a building block only confirms that this "
                    + "BONELAB project version can be supported; it does not mean "
                    + "the toolkit can generate that feature. The rows below show "
                    + "actual generation coverage.")
                .Append("\nBehaviour authoring: ")
                .Append(string.IsNullOrWhiteSpace(behaviourProfileDetail)
                    ? "No behaviour profile preflight detail was produced."
                    : behaviourProfileDetail)
                .Append("\nBody-grab authoring: ")
                .Append(string.IsNullOrWhiteSpace(gripProfileDetail)
                    ? "No body-grab preflight detail was produced."
                    : gripProfileDetail)
                .Append("\nGaze authoring: ")
                .Append(string.IsNullOrWhiteSpace(gazeProfileDetail)
                    ? "No gaze preflight detail was produced."
                    : gazeProfileDetail)
                .Append("\nPhysical Jaw authoring: ")
                .Append(string.IsNullOrWhiteSpace(jawProfileDetail)
                    ? "No Physical Jaw preflight detail was produced."
                    : jawProfileDetail)
                .Append("\nAudio authoring: ")
                .Append(string.IsNullOrWhiteSpace(audioProfileDetail)
                    ? "No audio preflight detail was produced."
                    : audioProfileDetail);

            foreach (CapabilityAudit audit in snapshot.Where(value => !value.IsAvailable))
            {
                detail.Append("\n").Append(audit.Label).Append(": ")
                    .Append(string.Join("; ", audit.Issues));
            }
            return detail.ToString();
        }

        private sealed class CapabilityAudit
        {
            private const BindingFlags DeclaredFields =
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly;

            public NpcCompatibilityCapabilities Capability { get; }
            public string Label { get; }
            public List<string> Issues { get; } = new List<string>();
            public bool IsAvailable => Issues.Count == 0;

            public CapabilityAudit(
                NpcCompatibilityCapabilities capability,
                string label)
            {
                Capability = capability;
                Label = label;
            }

            public Type RequireType(string fullName, string expectedAssembly)
            {
                Type type = ResolveType(fullName, expectedAssembly);
                if (type == null)
                {
                    Issues.Add("missing type " + fullName + " in " + expectedAssembly
                        + " (open Project Settings > Marrow NPC Toolkit > Patch 6 "
                        + "Behaviour and install/update the project declarations)");
                    return null;
                }

                string actualAssembly = type.Assembly.GetName().Name;
                if (!string.Equals(actualAssembly, expectedAssembly,
                    StringComparison.Ordinal))
                {
                    Issues.Add(fullName + " is in " + actualAssembly + ", expected "
                        + expectedAssembly + " (the prefab binding assembly will not match)");
                }
                return type;
            }

            public void RequireBaseType(Type type, string expectedBaseType)
            {
                if (type == null)
                    return;
                string actual = type.BaseType?.FullName ?? "<none>";
                if (!string.Equals(actual, expectedBaseType, StringComparison.Ordinal))
                {
                    Issues.Add(type.FullName + " must directly derive from "
                        + expectedBaseType + " (found " + actual + ")");
                }
            }

            public void RequireValueType(Type type)
            {
                if (type != null && !type.IsValueType)
                {
                    Issues.Add(type.FullName
                        + " must be a value type to match the Patch 6 typetree");
                }
            }

            public void RequireField(
                Type declaringType,
                string fieldName,
                string expectedFieldType)
            {
                if (declaringType == null)
                    return;

                FieldInfo field = declaringType.GetField(fieldName, DeclaredFields);
                if (field == null)
                {
                    Issues.Add("missing field " + declaringType.FullName + "." + fieldName
                        + " (expected " + expectedFieldType + ")");
                    return;
                }

                string actual = field.FieldType.FullName ?? field.FieldType.Name;
                if (!string.Equals(actual, expectedFieldType, StringComparison.Ordinal))
                {
                    Issues.Add(declaringType.FullName + "." + fieldName + " is " + actual
                        + ", expected " + expectedFieldType);
                }
            }

            public void RequireFieldExists(Type declaringType, string fieldName)
            {
                if (declaringType == null)
                    return;
                if (declaringType.GetField(fieldName, DeclaredFields) == null)
                    Issues.Add("missing field " + declaringType.FullName + "."
                        + fieldName);
            }

            public void RequireMethod(
                Type declaringType,
                string methodName,
                string expectedReturnType,
                params string[] expectedParameterTypes)
            {
                if (declaringType == null)
                    return;

                MethodInfo match = declaringType
                    .GetMethods(DeclaredFields)
                    .FirstOrDefault(method =>
                        string.Equals(method.Name, methodName, StringComparison.Ordinal)
                        && string.Equals(method.ReturnType.FullName, expectedReturnType,
                            StringComparison.Ordinal)
                        && method.GetParameters().Select(parameter =>
                            parameter.ParameterType.FullName ?? parameter.ParameterType.Name)
                            .SequenceEqual(expectedParameterTypes));
                if (match == null)
                {
                    Issues.Add("missing method " + declaringType.FullName + "." + methodName
                        + "(" + string.Join(", ", expectedParameterTypes) + ") -> "
                        + expectedReturnType);
                }
            }

            private static Type ResolveType(string fullName, string expectedAssembly)
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                foreach (Assembly assembly in assemblies)
                {
                    if (!string.Equals(assembly.GetName().Name, expectedAssembly,
                        StringComparison.Ordinal))
                        continue;
                    Type exact = assembly.GetType(fullName, false);
                    if (exact != null)
                        return exact;
                }

                foreach (Assembly assembly in assemblies)
                {
                    Type fallback = assembly.GetType(fullName, false);
                    if (fallback != null)
                        return fallback;
                }
                return null;
            }
        }
    }
}
