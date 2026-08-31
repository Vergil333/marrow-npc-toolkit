using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Vergil333.MarrowNpcToolkit.ProjectCompatibility
{
    internal sealed partial class MarrowNpcToolkitPatch6CompatibilityProbe
    {
        private const int NativeBodyLayer = 12;
        private const int NativeTrackerLayer = 26;
        private const string BeingTagBarcode = "SLZ.Marrow.BoneTag.Being";
        private const string BloodSurfaceBarcode =
            "SLZ.Backlot.SurfaceDataCard.Blood";
        private const int BloodColliderDecalType = -1;

        private static readonly IReadOnlyDictionary<HumanBodyBones, int>
            NativeSensorMuscleIndices =
                new Dictionary<HumanBodyBones, int>
                {
                    [HumanBodyBones.Hips] = 0,
                    [HumanBodyBones.LeftUpperLeg] = 1,
                    [HumanBodyBones.LeftFoot] = 3,
                    [HumanBodyBones.RightUpperLeg] = 4,
                    [HumanBodyBones.RightFoot] = 6,
                    [HumanBodyBones.Chest] = 8,
                };

        private static InteractionShell ConfigureInteractionShell(
            GameObject outputRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            Component entity,
            IReadOnlyDictionary<HumanBodyBones, Component> marrowBodies,
            Component puppet,
            Component poolee,
            Component brain,
            Component powerLegs,
            Component navAgent)
        {
            IReadOnlyList<HumanBodyBones> entityOrder = EntityOrderFor(roles);
            if (outputRoot == null || entity == null || puppet == null
                || poolee == null || brain == null || powerLegs == null
                || navAgent == null)
                throw new InvalidOperationException(
                    "The Patch 6 interaction pass requires the complete native "
                    + "root, AI, pooling, navigation, and PuppetMaster shell.");

            Type hostType = ResolvePatch6ComponentType(
                "SLZ.Marrow.InteractableHost", "SLZ.Marrow");
            Type hostManagerType = ResolvePatch6ComponentType(
                "SLZ.Marrow.InteractableHostManager", "SLZ.Marrow");
            Type trackerType = ResolvePatch6ComponentType(
                "SLZ.Marrow.Interaction.Tracker", "SLZ.Marrow");
            Type triggerProxyType = ResolvePatch6ComponentType(
                "SLZ.Marrow.AI.TriggerRefProxy", "SLZ.Marrow");
            Type sensorType = ResolvePatch6ComponentType(
                "SLZ.Marrow.PuppetMasta.MuscleCollisionBroadcasterSensor",
                "SLZ.Marrow");
            Type visualDamageType = ResolvePatch6ComponentType(
                "SLZ.Marrow.Combat.VisualDamageController", "SLZ.Marrow");
            Type impactPropertiesType = ResolvePatch6ComponentType(
                "SLZ.Marrow.ImpactProperties", "SLZ.Marrow");
            Type damageReceiverType = ResolvePatch6ComponentType(
                "SLZ.Combat.VisualDamageReceiver", "Assembly-CSharp");
            Type agentLinkType = ResolvePatch6ComponentType(
                "SLZ.Bonelab.AgentLinkControl", "Assembly-CSharp");

            Component hostManager = AddNative(
                outputRoot, hostManagerType, "InteractableHostManager");
            Component agentLink = AddNative(
                outputRoot, agentLinkType, "AgentLinkControl");
            Component visualDamage = AddNative(
                outputRoot, visualDamageType, "VisualDamageController");

            var hosts = new Dictionary<HumanBodyBones, Component>();
            var trackers = new Dictionary<HumanBodyBones, Component>();
            var trackerColliders = new Dictionary<HumanBodyBones, BoxCollider>();
            var impactProperties = new Dictionary<HumanBodyBones, Component>();
            var damageReceivers = new Dictionary<HumanBodyBones, Component>();
            foreach (HumanBodyBones role in entityOrder)
            {
                NativeRole nativeRole = roles[role];
                nativeRole.Body.gameObject.layer = NativeBodyLayer;

                Component impact = AddNative(
                    nativeRole.Body.gameObject,
                    impactPropertiesType,
                    role + " ImpactProperties");
                ConfigureImpactProperties(impact);
                impactProperties.Add(role, impact);

                Component receiver = AddNative(
                    nativeRole.Body.gameObject,
                    damageReceiverType,
                    role + " VisualDamageReceiver");
                ConfigureVisualDamageReceiver(receiver, visualDamage);
                damageReceivers.Add(role, receiver);

                Component host = AddNative(
                    nativeRole.Body.gameObject,
                    hostType,
                    role + " InteractableHost");
                ConfigureInteractableHost(host, entity);
                hosts.Add(role, host);

                var trackerObject = new GameObject(
                    TrackerName(role))
                {
                    layer = NativeTrackerLayer,
                };
                Transform trackerTransform = trackerObject.transform;
                trackerTransform.SetParent(nativeRole.Body, false);
                trackerTransform.localPosition = Vector3.zero;
                trackerTransform.localRotation = Quaternion.identity;
                trackerTransform.localScale = Vector3.one;

                BoxCollider trackerCollider = trackerObject.AddComponent<BoxCollider>();
                Bounds trackerBounds = ColliderBoundsInFrame(
                    nativeRole.Collider, nativeRole.Body);
                if (!IsFinite(trackerBounds.center)
                    || !IsFinite(trackerBounds.size)
                    || trackerBounds.size.x <= 0f
                    || trackerBounds.size.y <= 0f
                    || trackerBounds.size.z <= 0f)
                    throw new InvalidOperationException(
                        role + " produced invalid tracker bounds.");
                trackerCollider.center = trackerBounds.center;
                trackerCollider.size = trackerBounds.size;
                trackerCollider.isTrigger = false;
                trackerCollider.enabled = true;

                Component tracker = AddNative(
                    trackerObject, trackerType, role + " Tracker");
                var trackerObjectData = new SerializedObject(tracker);
                SetObject(trackerObjectData, "_entity", entity);
                SetObject(trackerObjectData, "_body", marrowBodies[role]);
                // The validated Patch 6 contract points Tracker._collider at
                // the body's registered primary collision shape. The sibling
                // BoxCollider is the broad Entity tracking volume, not the
                // damage/physics collider reference.
                SetObject(trackerObjectData, "_collider", nativeRole.Collider);
                trackerObjectData.ApplyModifiedPropertiesWithoutUndo();

                var bodyData = new SerializedObject(marrowBodies[role]);
                SetObjectArray(bodyData, "_trackers", new UnityEngine.Object[]
                {
                    tracker,
                });
                bodyData.ApplyModifiedPropertiesWithoutUndo();
                trackers.Add(role, tracker);
                trackerColliders.Add(role, trackerCollider);
            }

            ConfigureHostManager(
                hostManager,
                entityOrder.Select(role => hosts[role]));
            ConfigureEntityInteractionRegistry(
                entity, poolee, puppet, roles, hosts, outputRoot.transform.localScale);

            Component triggerProxy = ConfigureTriggerProxy(
                outputRoot, roles, triggerProxyType);
            Dictionary<HumanBodyBones, Component> sensors = ConfigureSensors(
                roles, puppet, sensorType);
            ConfigurePowerLegsInteraction(
                powerLegs, hostManager, triggerProxy, sensors);
            ConfigureBrainAndPooling(brain, powerLegs, puppet, poolee, entity);
            ConfigureAgentLink(
                agentLink,
                roles,
                brain,
                powerLegs,
                puppet,
                navAgent,
                triggerProxy);
            ConfigureVisualDamage(visualDamage, outputRoot);

            var result = new InteractionShell(
                hostManager,
                agentLink,
                triggerProxy,
                visualDamage,
                hosts,
                trackers,
                trackerColliders,
                impactProperties,
                damageReceivers,
                sensors);
            ValidateInteractionShell(
                outputRoot,
                roles,
                entity,
                marrowBodies,
                puppet,
                poolee,
                brain,
                powerLegs,
                navAgent,
                result);
            return result;
        }

        private static InteractionShell ResolveInteractionShell(
            GameObject outputRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles)
        {
            IReadOnlyList<HumanBodyBones> entityOrder = EntityOrderFor(roles);
            Type hostType = ResolvePatch6ComponentType(
                "SLZ.Marrow.InteractableHost", "SLZ.Marrow");
            Type hostManagerType = ResolvePatch6ComponentType(
                "SLZ.Marrow.InteractableHostManager", "SLZ.Marrow");
            Type trackerType = ResolvePatch6ComponentType(
                "SLZ.Marrow.Interaction.Tracker", "SLZ.Marrow");
            Type triggerProxyType = ResolvePatch6ComponentType(
                "SLZ.Marrow.AI.TriggerRefProxy", "SLZ.Marrow");
            Type sensorType = ResolvePatch6ComponentType(
                "SLZ.Marrow.PuppetMasta.MuscleCollisionBroadcasterSensor",
                "SLZ.Marrow");
            Type visualDamageType = ResolvePatch6ComponentType(
                "SLZ.Marrow.Combat.VisualDamageController", "SLZ.Marrow");
            Type impactPropertiesType = ResolvePatch6ComponentType(
                "SLZ.Marrow.ImpactProperties", "SLZ.Marrow");
            Type damageReceiverType = ResolvePatch6ComponentType(
                "SLZ.Combat.VisualDamageReceiver", "Assembly-CSharp");
            Type agentLinkType = ResolvePatch6ComponentType(
                "SLZ.Bonelab.AgentLinkControl", "Assembly-CSharp");
            Component hostManager = RequireOnlyComponent(
                outputRoot, hostManagerType, "InteractableHostManager");
            Component agentLink = RequireOnlyComponent(
                outputRoot, agentLinkType, "AgentLinkControl");
            Component triggerProxy = RequireOnlyComponent(
                roles[HumanBodyBones.Head].Body.gameObject,
                triggerProxyType,
                "Head TriggerRefProxy");
            Component visualDamage = RequireOnlyComponent(
                outputRoot, visualDamageType, "VisualDamageController");

            var hosts = new Dictionary<HumanBodyBones, Component>();
            var trackers = new Dictionary<HumanBodyBones, Component>();
            var trackerColliders = new Dictionary<HumanBodyBones, BoxCollider>();
            var impactProperties = new Dictionary<HumanBodyBones, Component>();
            var damageReceivers = new Dictionary<HumanBodyBones, Component>();
            foreach (HumanBodyBones role in entityOrder)
            {
                NativeRole nativeRole = roles[role];
                impactProperties.Add(
                    role,
                    RequireOnlyComponent(
                        nativeRole.Body.gameObject,
                        impactPropertiesType,
                        role + " ImpactProperties"));
                damageReceivers.Add(
                    role,
                    RequireOnlyComponent(
                        nativeRole.Body.gameObject,
                        damageReceiverType,
                        role + " VisualDamageReceiver"));
                hosts.Add(
                    role,
                    RequireOnlyComponent(
                        nativeRole.Body.gameObject,
                        hostType,
                        role + " InteractableHost"));
                Transform trackerTransform = nativeRole.Body.Find(
                    TrackerName(role));
                if (trackerTransform == null || trackerTransform.parent != nativeRole.Body)
                    throw new InvalidOperationException(
                        role + " has no direct native Entity tracker child.");
                trackers.Add(
                    role,
                    RequireOnlyComponent(
                        trackerTransform.gameObject,
                        trackerType,
                        role + " Tracker"));
                BoxCollider[] boxes = trackerTransform.GetComponents<BoxCollider>();
                if (boxes.Length != 1)
                    throw new InvalidOperationException(
                        role + " Entity tracker does not have exactly one BoxCollider.");
                trackerColliders.Add(role, boxes[0]);
            }

            var sensors = new Dictionary<HumanBodyBones, Component>();
            foreach (HumanBodyBones role in NativeSensorMuscleIndices.Keys)
                sensors.Add(
                    role,
                    RequireOnlyComponent(
                        roles[role].Body.gameObject,
                        sensorType,
                        role + " balance sensor"));

            return new InteractionShell(
                hostManager,
                agentLink,
                triggerProxy,
                visualDamage,
                hosts,
                trackers,
                trackerColliders,
                impactProperties,
                damageReceivers,
                sensors);
        }

        private static void ValidateInteractionShell(
            GameObject outputRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            Component entity,
            IReadOnlyDictionary<HumanBodyBones, Component> marrowBodies,
            Component puppet,
            Component poolee,
            Component brain,
            Component powerLegs,
            Component navAgent,
            InteractionShell shell)
        {
            IReadOnlyList<HumanBodyBones> entityOrder = EntityOrderFor(roles);
            int expectedCount = entityOrder.Count;
            if (shell == null)
                throw new InvalidOperationException(
                    "The Patch 6 interaction shell was not resolved.");
            if (shell.Hosts.Count != expectedCount
                || shell.Trackers.Count != expectedCount
                || shell.TrackerColliders.Count != expectedCount
                || shell.ImpactProperties.Count != expectedCount
                || shell.DamageReceivers.Count != expectedCount
                || shell.Sensors.Count != 6)
                throw new InvalidOperationException(
                    "The interaction shell must contain " + expectedCount
                    + " hosts, " + expectedCount + " Entity trackers, "
                    + expectedCount + " blood-impact receivers, and six balance "
                    + "sensors.");

            var manager = new SerializedObject(shell.HostManager);
            SerializedProperty managerHosts = Require(manager, "hosts");
            SerializedProperty grabbedHosts = Require(manager, "grabbedHosts");
            if (managerHosts.arraySize != expectedCount
                || grabbedHosts.arraySize != 0)
                throw new InvalidOperationException(
                    "InteractableHostManager does not contain the exact host arrays.");
            for (int index = 0; index < entityOrder.Count; index++)
                if (managerHosts.GetArrayElementAtIndex(index).objectReferenceValue
                    != shell.Hosts[entityOrder[index]])
                    throw new InvalidOperationException(
                        "InteractableHostManager host order differs at "
                        + entityOrder[index] + ".");

            foreach (HumanBodyBones role in entityOrder)
            {
                NativeRole nativeRole = roles[role];
                Component host = shell.Hosts[role];
                Component tracker = shell.Trackers[role];
                BoxCollider trackerCollider = shell.TrackerColliders[role];
                Component impact = shell.ImpactProperties[role];
                Component receiver = shell.DamageReceivers[role];
                var hostData = new SerializedObject(host);
                var trackerData = new SerializedObject(tracker);
                var bodyData = new SerializedObject(marrowBodies[role]);
                if (nativeRole.Body.gameObject.layer != NativeBodyLayer
                    || tracker.gameObject.layer != NativeTrackerLayer
                    || tracker.transform.parent != nativeRole.Body
                    || trackerCollider.isTrigger || !trackerCollider.enabled
                    || Require(hostData, "marrowEntity").objectReferenceValue != entity
                    // The validated Patch 6 reference leaves the per-host manager pointer
                    // null and registers all hosts centrally on the root
                    // InteractableHostManager.
                    || Require(hostData, "manager").objectReferenceValue != null
                    || Require(trackerData, "_entity").objectReferenceValue != entity
                    || Require(trackerData, "_body").objectReferenceValue
                        != marrowBodies[role]
                    || Require(trackerData, "_collider").objectReferenceValue
                        != nativeRole.Collider
                    || Require(bodyData, "_trackers").arraySize != 1
                    || Require(bodyData, "_trackers")
                        .GetArrayElementAtIndex(0).objectReferenceValue != tracker)
                    throw new InvalidOperationException(
                        role + " has an incomplete host/tracker reference contract.");

                Bounds expected = ColliderBoundsInFrame(
                    nativeRole.Collider, nativeRole.Body);
                if (Vector3.Distance(trackerCollider.center, expected.center) > 0.00001f
                    || Vector3.Distance(trackerCollider.size, expected.size) > 0.00001f)
                    throw new InvalidOperationException(
                        role + " Entity tracker does not match its primary body bounds.");

                ValidateImpactProperties(role, impact);
                ValidateVisualDamageReceiver(role, receiver, shell.VisualDamage);
            }

            var entityData = new SerializedObject(entity);
            if (Require(entityData, "_poolee").objectReferenceValue != poolee
                || Require(entityData, "_behaviours").arraySize
                    != expectedCount + 1)
                throw new InvalidOperationException(
                    "MarrowEntity does not register Poolee, PuppetMaster, and "
                    + expectedCount + " hosts.");
            SerializedProperty behaviours = Require(entityData, "_behaviours");
            if (behaviours.GetArrayElementAtIndex(0).objectReferenceValue != puppet)
                throw new InvalidOperationException(
                    "MarrowEntity behaviour slot zero is not PuppetMaster.");
            for (int index = 0; index < entityOrder.Count; index++)
                if (behaviours.GetArrayElementAtIndex(index + 1).objectReferenceValue
                    != shell.Hosts[entityOrder[index]])
                    throw new InvalidOperationException(
                        "MarrowEntity host registry differs at "
                        + entityOrder[index] + ".");

            ValidateTriggerProxy(outputRoot, roles, shell.TriggerProxy);
            ValidateSensors(roles, puppet, shell.Sensors);
            ValidatePowerLegsInteraction(
                powerLegs, shell.HostManager, shell.TriggerProxy, shell.Sensors);
            ValidateBrainAndPooling(brain, powerLegs, puppet, poolee, entity);
            ValidateAgentLink(
                roles,
                shell.AgentLink,
                brain,
                powerLegs,
                puppet,
                navAgent,
                shell.TriggerProxy);
            ValidateVisualDamage(shell.VisualDamage, outputRoot);
        }

        private static void AppendInteractionFingerprint(
            StringBuilder text,
            GameObject outputRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            InteractionShell shell)
        {
            IReadOnlyList<HumanBodyBones> entityOrder = EntityOrderFor(roles);
            text.Append("interaction=")
                .Append(RelativePath(outputRoot.transform, shell.HostManager.transform))
                .Append(',')
                .Append(RelativePath(outputRoot.transform, shell.AgentLink.transform))
                .Append(',')
                .Append(RelativePath(outputRoot.transform, shell.TriggerProxy.transform))
                .Append('|');
            foreach (HumanBodyBones role in entityOrder)
            {
                BoxCollider box = shell.TrackerColliders[role];
                text.Append(role).Append(':')
                    .Append(RelativePath(outputRoot.transform, shell.Hosts[role].transform))
                    .Append(':')
                    .Append(RelativePath(outputRoot.transform, shell.Trackers[role].transform))
                    .Append(':');
                AppendVector(text, box.center);
                AppendVector(text, box.size);
                var impact = new SerializedObject(shell.ImpactProperties[role]);
                text.Append(':')
                    .Append(Require(impact, "_surfaceDataCard._barcode._id")
                        .stringValue)
                    .Append(':')
                    .Append(Require(impact, "decalType").intValue)
                    .Append(':')
                    .Append(RelativePath(
                        outputRoot.transform,
                        shell.DamageReceivers[role].transform))
                    .Append('|');
            }
            foreach (KeyValuePair<HumanBodyBones, int> sensor
                     in NativeSensorMuscleIndices.OrderBy(value => value.Value))
            {
                var data = new SerializedObject(shell.Sensors[sensor.Key]);
                text.Append("sensor=").Append(sensor.Key).Append(':')
                    .Append(sensor.Value).Append(':')
                    .Append(F(Require(data, "totalMass").floatValue)).Append('|');
            }
            SerializedProperty allBodies = Require(
                new SerializedObject(shell.AgentLink), "allRBs");
            for (int index = 0; index < entityOrder.Count; index++)
                text.Append("agentBody=").Append(index).Append(':')
                    .Append(entityOrder[index]).Append(':')
                    .Append(RelativePath(
                        outputRoot.transform,
                        ((Rigidbody)allBodies.GetArrayElementAtIndex(index)
                            .objectReferenceValue).transform)).Append('|');
        }

        private static void ConfigureInteractableHost(
            Component host,
            Component entity)
        {
            var data = new SerializedObject(host);
            SerializedProperty controller = Require(
                data, "<VirtualController>k__BackingField");
            SerializedProperty defaults = RequireRelative(
                controller, "defaultSettings");
            SetRelativeFloat(defaults, "lookRotationWeight", 1f);
            SetRelativeFloat(defaults, "handTwistWeight", 0.5f);
            SetRelativeFloat(defaults, "handSwingWeight", 1f);
            SetRelativeFloat(defaults, "positionWeight", 0.5f);
            SetRelativeFloat(defaults, "jointSwingLimit", 90f);
            SetRelativeFloat(defaults, "jointTwistLimit", 90f);
            SetRelativeBool(defaults, "autoTargetUpdatePrimary", false);
            SetRelativeBool(defaults, "dynamicHandDistanceWeights", false);
            SerializedProperty overrideTransform = RequireRelative(
                controller, "overrideVCTransform");
            SetRelativeVector(overrideTransform, "position", Vector3.zero);
            SetRelativeQuaternion(
                overrideTransform, "rotation", Quaternion.identity);
            SetObject(data, "marrowEntity", entity);
            SetObject(data, "manager", null);
            SetInt(data, "ignoreBodyOnGrab", 0);
            SetInt(data, "<IsStatic>k__BackingField", 0);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureHostManager(
            Component manager,
            IEnumerable<Component> hosts)
        {
            var data = new SerializedObject(manager);
            SetObjectArray(
                data,
                "hosts",
                hosts.Cast<UnityEngine.Object>().ToArray());
            SetObjectArray(
                data, "grabbedHosts", Array.Empty<UnityEngine.Object>());
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureEntityInteractionRegistry(
            Component entity,
            Component poolee,
            Component puppet,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            IReadOnlyDictionary<HumanBodyBones, Component> hosts,
            Vector3 originalScale)
        {
            IReadOnlyList<HumanBodyBones> entityOrder = EntityOrderFor(roles);
            var data = new SerializedObject(entity);
            SetObject(data, "_poolee", poolee);
            var behaviours = new List<UnityEngine.Object> { puppet };
            behaviours.AddRange(
                entityOrder.Select(role =>
                    (UnityEngine.Object)hosts[role]));
            SetObjectArray(data, "_behaviours", behaviours.ToArray());
            SetVector(data, "_originalScale", originalScale);

            SerializedProperty tags = RequireRelative(
                Require(data, "_tags"), "_tags");
            tags.arraySize = 1;
            SerializedProperty barcode = RequireRelative(
                tags.GetArrayElementAtIndex(0), "_barcode");
            SetRelativeString(barcode, "_id", BeingTagBarcode);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Component ConfigureTriggerProxy(
            GameObject outputRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            Type triggerProxyType)
        {
            NativeRole head = roles[HumanBodyBones.Head];
            Component proxy = AddNative(
                head.Body.gameObject, triggerProxyType, "Head TriggerRefProxy");
            Transform legacyProxy = outputRoot.transform.Find("Legacy_Proxy");
            if (legacyProxy == null)
            {
                var legacyObject = new GameObject("Legacy_Proxy");
                legacyProxy = legacyObject.transform;
                legacyProxy.SetParent(outputRoot.transform, false);
            }

            var data = new SerializedObject(proxy);
            SetInt(data, "triggerType", 2);
            SetInt(data, "npcType", 1);
            SetInt(data, "teamNumber", 0);
            SetObject(data, "root", outputRoot);
            SetObject(data, "targetHead", head.Rigidbody);
            SetObject(data, "lfHandRb", roles[HumanBodyBones.LeftHand].Rigidbody);
            SetObject(data, "rtHandRb", roles[HumanBodyBones.RightHand].Rigidbody);
            SetObject(data, "chestTran", roles[HumanBodyBones.Chest].Body);
            SetObject(data, "feetTran", roles[HumanBodyBones.LeftFoot].Body);
            SetObject(data, "legacyProxy", legacyProxy);
            data.ApplyModifiedPropertiesWithoutUndo();
            return proxy;
        }

        private static Dictionary<HumanBodyBones, Component> ConfigureSensors(
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            Component puppet,
            Type sensorType)
        {
            float totalMass = EntityOrderFor(roles).Sum(
                role => roles[role].Rigidbody.mass);
            if (!IsFinite(totalMass) || totalMass <= 0f)
                throw new InvalidOperationException(
                    "The NPC has no valid total body mass for its balance sensors.");

            var result = new Dictionary<HumanBodyBones, Component>();
            foreach (KeyValuePair<HumanBodyBones, int> pair
                     in NativeSensorMuscleIndices.OrderBy(value => value.Value))
            {
                Component sensor = AddNative(
                    roles[pair.Key].Body.gameObject,
                    sensorType,
                    pair.Key + " balance sensor");
                var data = new SerializedObject(sensor);
                SetObject(data, "puppetMaster", puppet);
                SetInt(data, "muscleIndex", pair.Value);
                SetInt(data, "isGrounded", 0);
                SetVector(data, "groundNormal", Vector3.zero);
                SetVector(data, "_totalImpulse", Vector3.zero);
                SetFloat(data, "totalMass", totalMass);
                SetFloat(data, "additionalMass", 0f);
                data.ApplyModifiedPropertiesWithoutUndo();
                result.Add(pair.Key, sensor);
            }
            return result;
        }

        private static void ConfigurePowerLegsInteraction(
            Component powerLegs,
            Component hostManager,
            Component triggerProxy,
            IReadOnlyDictionary<HumanBodyBones, Component> sensors)
        {
            var data = new SerializedObject(powerLegs);
            SetObject(data, "hostManager", hostManager);
            SetObject(data, "sensors.selfTrp", triggerProxy);
            SetObjectArray(
                data,
                "sensors.forceSensorsFeet",
                new UnityEngine.Object[]
                {
                    sensors[HumanBodyBones.LeftFoot],
                    sensors[HumanBodyBones.RightFoot],
                });
            SetObjectArray(
                data,
                "sensors.forceSensorsHands",
                Array.Empty<UnityEngine.Object>());
            SetObjectArray(
                data,
                "sensors.forceSensorsBody",
                new UnityEngine.Object[]
                {
                    sensors[HumanBodyBones.Hips],
                    sensors[HumanBodyBones.LeftUpperLeg],
                    sensors[HumanBodyBones.RightUpperLeg],
                    sensors[HumanBodyBones.Chest],
                });
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureBrainAndPooling(
            Component brain,
            Component powerLegs,
            Component puppet,
            Component poolee,
            Component entity)
        {
            var brainData = new SerializedObject(brain);
            SetObject(brainData, "_poolee", poolee);
            SetObject(brainData, "behaviour", powerLegs);
            SetObject(brainData, "puppetMaster", puppet);
            SetInt(brainData, "dontClearBaseConfig", 1);
            SetInt(brainData, "isDead", 0);
            brainData.ApplyModifiedPropertiesWithoutUndo();

            var powerData = new SerializedObject(powerLegs);
            SetObject(powerData, "puppetMaster", puppet);
            SetObject(powerData, "_poolee", poolee);
            powerData.ApplyModifiedPropertiesWithoutUndo();

            var puppetData = new SerializedObject(puppet);
            SetObject(puppetData, "_poolee", poolee);
            puppetData.ApplyModifiedPropertiesWithoutUndo();

            var entityData = new SerializedObject(entity);
            SetObject(entityData, "_poolee", poolee);
            entityData.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureAgentLink(
            Component link,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            Component brain,
            Component powerLegs,
            Component puppet,
            Component navAgent,
            Component triggerProxy)
        {
            if (link is Behaviour behaviour) behaviour.enabled = false;
            var data = new SerializedObject(link);
            SetFloat(data, "totalMass", 0f);
            SetFloat(data, "jointForceMult", 1f);
            SetObject(data, "navAgent", navAgent);
            SetObject(data, "brain", brain);
            SetObject(data, "triggerProxy", triggerProxy);
            SetObject(data, "baseBehaviour", powerLegs);
            SetObject(data, "legBehaviour", powerLegs);
            SetObject(data, "_puppet", puppet);
            SetFloat(data, "minLinkDupeDuration", 2f);
            SetObject(data, "headRB", roles[HumanBodyBones.Head].Rigidbody);
            SetObject(data, "chestRB", roles[HumanBodyBones.Chest].Rigidbody);
            SetObject(data, "leftHandRB", roles[HumanBodyBones.LeftHand].Rigidbody);
            SetObject(data, "leftElbowRB",
                roles[HumanBodyBones.LeftLowerArm].Rigidbody);
            SetObject(data, "rightHandRB", roles[HumanBodyBones.RightHand].Rigidbody);
            SetObject(data, "rightElbowRB",
                roles[HumanBodyBones.RightLowerArm].Rigidbody);
            SetObject(data, "leftFootRB", roles[HumanBodyBones.LeftFoot].Rigidbody);
            SetObject(data, "leftKneeRB",
                roles[HumanBodyBones.LeftLowerLeg].Rigidbody);
            SetObject(data, "rightFootRB", roles[HumanBodyBones.RightFoot].Rigidbody);
            SetObject(data, "rightKneeRB",
                roles[HumanBodyBones.RightLowerLeg].Rigidbody);
            SetObjectArray(
                data,
                "allRBs",
                EntityOrderFor(roles).Select(role =>
                    (UnityEngine.Object)roles[role].Rigidbody).ToArray());
            foreach (string field in new[]
            {
                "frozenCrabJumpTargetObj", "zipStick", "zipGripBody", "owner",
                "headJoint", "chestJoint", "leftElbowJoint", "rightElbowJoint",
                "leftHandJoint", "rightHandJoint", "leftKneeJoint",
                "rightKneeJoint", "leftFootJoint", "rightFootJoint", "playerProxy",
            })
                SetObject(data, field, null);
            SetInt(data, "linkState", 0);
            SetInt(data, "isOnLink", 0);
            SetInt(data, "isZipping", 0);
            SetVector(data, "initialPos", Vector3.zero);
            SetFloat(data, "distTimer", 5f);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureVisualDamage(
            Component visualDamage,
            GameObject outputRoot)
        {
            Renderer[] renderers = outputRoot.transform.Find("AnimationRoot")
                ?.GetComponentsInChildren<Renderer>(true)
                ?? Array.Empty<Renderer>();
            if (renderers.Length == 0)
                throw new InvalidOperationException(
                    "VisualDamageController cannot find the preserved Avatar renderers.");
            var data = new SerializedObject(visualDamage);
            SetObjectArray(
                data,
                "Renderers",
                renderers.Cast<UnityEngine.Object>().ToArray());
            SetFloat(data, "meshScaleFactor", 1f);
            SetFloat(data, "hitScaleFactor", 1f);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureImpactProperties(Component impactProperties)
        {
            var data = new SerializedObject(impactProperties);
            SerializedProperty card = Require(data, "_surfaceDataCard");
            SerializedProperty barcode = RequireRelative(card, "_barcode");
            SetRelativeString(barcode, "_id", BloodSurfaceBarcode);
            SetInt(data, "decalType", BloodColliderDecalType);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureVisualDamageReceiver(
            Component receiver,
            Component visualDamage)
        {
            var data = new SerializedObject(receiver);
            SetVector(data, "orgpos", Vector3.zero);
            Require(data, "orgrot").quaternionValue = new Quaternion(0f, 0f, 0f, 0f);
            SetVector(data, "orgScale", Vector3.zero);
            SetObject(data, "bone", null);
            SetObject(data, "visualDamageController", visualDamage);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ValidateTriggerProxy(
            GameObject outputRoot,
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            Component proxy)
        {
            var data = new SerializedObject(proxy);
            if (Require(data, "triggerType").intValue != 2
                || Require(data, "npcType").intValue != 1
                || Require(data, "root").objectReferenceValue != outputRoot
                || Require(data, "targetHead").objectReferenceValue
                    != roles[HumanBodyBones.Head].Rigidbody
                || Require(data, "lfHandRb").objectReferenceValue
                    != roles[HumanBodyBones.LeftHand].Rigidbody
                || Require(data, "rtHandRb").objectReferenceValue
                    != roles[HumanBodyBones.RightHand].Rigidbody
                || Require(data, "chestTran").objectReferenceValue
                    != roles[HumanBodyBones.Chest].Body
                || Require(data, "legacyProxy").objectReferenceValue == null)
                throw new InvalidOperationException(
                    "Head TriggerRefProxy has an incomplete targeting contract.");
        }

        private static void ValidateSensors(
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            Component puppet,
            IReadOnlyDictionary<HumanBodyBones, Component> sensors)
        {
            float expectedTotalMass = EntityOrderFor(roles).Sum(
                role => roles[role].Rigidbody.mass);
            if (roles.ContainsKey(HumanBodyBones.Jaw)
                && Math.Abs(expectedTotalMass - 65f) > JawTolerance)
                throw new InvalidOperationException(
                    "The accepted 17-body Physical Jaw shell must total exactly 65 kg.");
            foreach (KeyValuePair<HumanBodyBones, int> expected
                     in NativeSensorMuscleIndices.OrderBy(value => value.Value))
            {
                var data = new SerializedObject(sensors[expected.Key]);
                if (Require(data, "puppetMaster").objectReferenceValue != puppet
                    || Require(data, "muscleIndex").intValue != expected.Value
                    || Math.Abs(Require(data, "totalMass").floatValue
                        - expectedTotalMass) > JawTolerance)
                    throw new InvalidOperationException(
                        expected.Key + " balance sensor is incomplete.");
            }
        }

        private static void ValidatePowerLegsInteraction(
            Component powerLegs,
            Component hostManager,
            Component triggerProxy,
            IReadOnlyDictionary<HumanBodyBones, Component> sensors)
        {
            var data = new SerializedObject(powerLegs);
            if (Require(data, "hostManager").objectReferenceValue != hostManager
                || Require(data, "sensors.selfTrp").objectReferenceValue
                    != triggerProxy)
                throw new InvalidOperationException(
                    "PowerLegs is not wired to its host manager and self proxy.");
            ValidateObjectArray(
                data,
                "sensors.forceSensorsFeet",
                sensors[HumanBodyBones.LeftFoot],
                sensors[HumanBodyBones.RightFoot]);
            ValidateObjectArray(data, "sensors.forceSensorsHands");
            ValidateObjectArray(
                data,
                "sensors.forceSensorsBody",
                sensors[HumanBodyBones.Hips],
                sensors[HumanBodyBones.LeftUpperLeg],
                sensors[HumanBodyBones.RightUpperLeg],
                sensors[HumanBodyBones.Chest]);
        }

        private static void ValidateBrainAndPooling(
            Component brain,
            Component powerLegs,
            Component puppet,
            Component poolee,
            Component entity)
        {
            var brainData = new SerializedObject(brain);
            var powerData = new SerializedObject(powerLegs);
            var puppetData = new SerializedObject(puppet);
            var entityData = new SerializedObject(entity);
            if (Require(brainData, "_poolee").objectReferenceValue != poolee
                || Require(brainData, "behaviour").objectReferenceValue != powerLegs
                || Require(brainData, "puppetMaster").objectReferenceValue != puppet
                || Require(brainData, "dontClearBaseConfig").intValue != 1
                || Require(powerData, "_poolee").objectReferenceValue != poolee
                || Require(powerData, "puppetMaster").objectReferenceValue != puppet
                || Require(puppetData, "_poolee").objectReferenceValue != poolee
                || Require(entityData, "_poolee").objectReferenceValue != poolee)
                throw new InvalidOperationException(
                    "The AI, PowerLegs, PuppetMaster, Entity, and Poolee cycle "
                    + "is incomplete.");
        }

        private static void ValidateAgentLink(
            IReadOnlyDictionary<HumanBodyBones, NativeRole> roles,
            Component link,
            Component brain,
            Component powerLegs,
            Component puppet,
            Component navAgent,
            Component triggerProxy)
        {
            IReadOnlyList<HumanBodyBones> entityOrder = EntityOrderFor(roles);
            if (link is Behaviour behaviour && behaviour.enabled)
                throw new InvalidOperationException(
                    "AgentLinkControl must remain disabled for this baseline.");
            var data = new SerializedObject(link);
            if (Require(data, "navAgent").objectReferenceValue != navAgent
                || Require(data, "brain").objectReferenceValue != brain
                || Require(data, "triggerProxy").objectReferenceValue != triggerProxy
                || Require(data, "baseBehaviour").objectReferenceValue != powerLegs
                || Require(data, "legBehaviour").objectReferenceValue != powerLegs
                || Require(data, "_puppet").objectReferenceValue != puppet)
                throw new InvalidOperationException(
                    "AgentLinkControl has incomplete controller references.");
            SerializedProperty allBodies = Require(data, "allRBs");
            if (allBodies.arraySize != entityOrder.Count)
                throw new InvalidOperationException(
                    "AgentLinkControl does not register all " + entityOrder.Count
                    + " rigidbodies.");
            for (int index = 0; index < entityOrder.Count; index++)
                if (allBodies.GetArrayElementAtIndex(index).objectReferenceValue
                    != roles[entityOrder[index]].Rigidbody)
                    throw new InvalidOperationException(
                        "AgentLinkControl body order differs at "
                        + entityOrder[index] + ".");
        }

        private static void ValidateVisualDamage(
            Component visualDamage,
            GameObject outputRoot)
        {
            Renderer[] expected = outputRoot.transform.Find("AnimationRoot")
                ?.GetComponentsInChildren<Renderer>(true)
                ?? Array.Empty<Renderer>();
            var data = new SerializedObject(visualDamage);
            SerializedProperty renderers = Require(data, "Renderers");
            if (expected.Length == 0 || renderers.arraySize != expected.Length)
                throw new InvalidOperationException(
                    "VisualDamageController renderer registry is incomplete.");
            for (int index = 0; index < expected.Length; index++)
                if (renderers.GetArrayElementAtIndex(index).objectReferenceValue
                    != expected[index])
                    throw new InvalidOperationException(
                        "VisualDamageController renderer order differs at " + index + ".");
        }

        private static void ValidateImpactProperties(
            HumanBodyBones role,
            Component impactProperties)
        {
            var data = new SerializedObject(impactProperties);
            if (!string.Equals(
                    Require(data, "_surfaceDataCard._barcode._id").stringValue,
                    BloodSurfaceBarcode,
                    StringComparison.Ordinal)
                || Require(data, "decalType").intValue != BloodColliderDecalType)
                throw new InvalidOperationException(
                    role + " does not use the native blood-impact surface contract.");
        }

        private static void ValidateVisualDamageReceiver(
            HumanBodyBones role,
            Component receiver,
            Component visualDamage)
        {
            var data = new SerializedObject(receiver);
            if (Require(data, "bone").objectReferenceValue != null
                || Require(data, "visualDamageController").objectReferenceValue
                    != visualDamage)
                throw new InvalidOperationException(
                    role + " VisualDamageReceiver is not wired to the root "
                    + "VisualDamageController.");
        }

        private static void ValidateObjectArray(
            SerializedObject data,
            string path,
            params UnityEngine.Object[] expected)
        {
            SerializedProperty array = Require(data, path);
            if (!array.isArray || array.arraySize != expected.Length)
                throw new InvalidOperationException(
                    path + " does not contain the expected references.");
            for (int index = 0; index < expected.Length; index++)
                if (array.GetArrayElementAtIndex(index).objectReferenceValue
                    != expected[index])
                    throw new InvalidOperationException(
                        path + " differs at index " + index + ".");
        }

        private static string TrackerName(HumanBodyBones role)
        {
            return role == HumanBodyBones.Jaw
                ? "Tracker[Jaw_M] Entity"
                : "Tracker[" + role + "] Entity";
        }

        private static Type ResolvePatch6ComponentType(
            string fullName,
            string assemblyName)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Where(value => string.Equals(
                    value.GetName().Name, assemblyName, StringComparison.Ordinal))
                .Select(value => value.GetType(fullName, false))
                .FirstOrDefault(value => value != null);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
                throw new TypeLoadException(
                    fullName + " is unavailable from " + assemblyName + ".");
            return type;
        }

        private sealed class InteractionShell
        {
            public Component HostManager { get; }
            public Component AgentLink { get; }
            public Component TriggerProxy { get; }
            public Component VisualDamage { get; }
            public IReadOnlyDictionary<HumanBodyBones, Component> Hosts { get; }
            public IReadOnlyDictionary<HumanBodyBones, Component> Trackers { get; }
            public IReadOnlyDictionary<HumanBodyBones, BoxCollider> TrackerColliders
            {
                get;
            }
            public IReadOnlyDictionary<HumanBodyBones, Component> ImpactProperties
            {
                get;
            }
            public IReadOnlyDictionary<HumanBodyBones, Component> DamageReceivers
            {
                get;
            }
            public IReadOnlyDictionary<HumanBodyBones, Component> Sensors { get; }

            public InteractionShell(
                Component hostManager,
                Component agentLink,
                Component triggerProxy,
                Component visualDamage,
                IReadOnlyDictionary<HumanBodyBones, Component> hosts,
                IReadOnlyDictionary<HumanBodyBones, Component> trackers,
                IReadOnlyDictionary<HumanBodyBones, BoxCollider> trackerColliders,
                IReadOnlyDictionary<HumanBodyBones, Component> impactProperties,
                IReadOnlyDictionary<HumanBodyBones, Component> damageReceivers,
                IReadOnlyDictionary<HumanBodyBones, Component> sensors)
            {
                HostManager = hostManager;
                AgentLink = agentLink;
                TriggerProxy = triggerProxy;
                VisualDamage = visualDamage;
                Hosts = hosts;
                Trackers = trackers;
                TrackerColliders = trackerColliders;
                ImpactProperties = impactProperties;
                DamageReceivers = damageReceivers;
                Sensors = sensors;
            }
        }
    }
}
