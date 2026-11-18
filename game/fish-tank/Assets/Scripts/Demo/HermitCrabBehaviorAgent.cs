using System.Collections.Generic;
using UnityEngine;

namespace CyberFishTank.Demo
{
    /// <summary>
    /// Lightweight freshwater hermit crab behavior for the visual slice. It crawls
    /// along the substrate, searches moss for detritus, inspects empty shells, rests,
    /// and withdraws into its shell when fish approach closely.
    /// </summary>
    public sealed class HermitCrabBehaviorAgent : MonoBehaviour
    {
        private enum Activity
        {
            Roam,
            Forage,
            InspectShell,
            Rest,
            Hide
        }

        private readonly List<Transform> forageSites = new List<Transform>();
        private readonly List<Transform> shellSites = new List<Transform>();
        private readonly List<Transform> fishRoots = new List<Transform>();
        private readonly List<Renderer> bodyRenderers = new List<Renderer>();
        private readonly List<Renderer> shellRenderers = new List<Renderer>();
        private readonly List<LegPose> walkingLegs = new List<LegPose>();
        private Transform antennae;
        private Quaternion antennaeRestRotation;

        private Activity activity = Activity.Roam;
        private Activity destinationActivity = Activity.Rest;
        private Vector3 homePosition;
        private Vector3 destination;
        private Vector3 heading = Vector3.forward;
        private Quaternion modelAxisCorrection;
        private float activityTimer;
        private float hunger = 0.62f;
        private float clock;
        private bool isHidden;
        private float movementAmount;

        private const float PatrolRadiusX = 1.25f;
        private const float PatrolRadiusZ = 0.52f;
        private const float CrawlSpeed = 0.065f;
        private const float HideDistance = 0.58f;

        public string CurrentActivity => activity.ToString();
        public float Hunger => hunger;

        private void Start()
        {
            homePosition = transform.position;
            modelAxisCorrection = Quaternion.LookRotation(Vector3.up, Vector3.left);
            CacheWorldSites();
            CacheFish();
            CacheShellParts();
            destinationActivity = Activity.Rest;
            PickRoamDestination();
        }

        private void Update()
        {
            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;

            clock += deltaTime;
            hunger = Mathf.Min(1f, hunger + deltaTime / 300f);
            RefreshFishCacheIfNeeded();

            if (ShouldHide())
            {
                EnterHide();
            }

            if (activity == Activity.Hide)
            {
                movementAmount = 0f;
                AnimateWalkingLegs(deltaTime);
                activityTimer -= deltaTime;
                if (activityTimer <= 0f && !FishNearby())
                {
                    SetHidden(false);
                    PickRoamDestination();
                }
                return;
            }

            if (activity == Activity.Forage || activity == Activity.InspectShell || activity == Activity.Rest)
            {
                movementAmount = 0f;
                activityTimer -= deltaTime;
                AnimateIdle(deltaTime);
                if (activityTimer <= 0f) PickRoamDestination();
                return;
            }

            CrawlTowardDestination(deltaTime);
        }

        private void CacheWorldSites()
        {
            forageSites.Clear();
            shellSites.Clear();
            var transforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            for (var i = 0; i < transforms.Length; i++)
            {
                var candidate = transforms[i];
                if (candidate == transform || candidate.IsChildOf(transform)) continue;
                var name = candidate.name;
                if (name.Contains("青苔") || name.Contains("Moss"))
                    forageSites.Add(candidate);
                else if (name.Contains("螺壳") || name.Contains("GastropodShell"))
                    shellSites.Add(candidate);
            }
        }

        private void CacheFish()
        {
            fishRoots.Clear();
            var transforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
            for (var i = 0; i < transforms.Length; i++)
            {
                var candidate = transforms[i];
                if (candidate.name == "孔雀鱼" || candidate.name == "霓虹灯鱼" || candidate.name == "熊猫鼠")
                    fishRoots.Add(candidate);
            }
        }

        private void RefreshFishCacheIfNeeded()
        {
            for (var i = fishRoots.Count - 1; i >= 0; i--)
            {
                if (fishRoots[i] == null) fishRoots.RemoveAt(i);
            }
            if (fishRoots.Count == 0) CacheFish();
        }

        private void CacheShellParts()
        {
            bodyRenderers.Clear();
            shellRenderers.Clear();
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var partName = renderers[i].name.ToLowerInvariant();
                if (partName.Contains("shell")) shellRenderers.Add(renderers[i]);
                else bodyRenderers.Add(renderers[i]);
            }
            var transforms = GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (!transforms[i].name.ToLowerInvariant().Contains("antennae")) continue;
                antennae = transforms[i];
                antennaeRestRotation = antennae.localRotation;
            }

            walkingLegs.Clear();
            for (var i = 0; i < transforms.Length; i++)
            {
                var leg = transforms[i];
                if (!leg.name.ToLowerInvariant().Contains("walkingleg")) continue;
                var side = Mathf.Sign(leg.localPosition.y);
                var positionRank = Mathf.Abs(leg.localPosition.x);
                var phase = positionRank * 11f;
                walkingLegs.Add(new LegPose(leg, leg.localRotation, phase, side));
            }
        }

        private bool ShouldHide()
        {
            return activity != Activity.Hide && FishNearby();
        }

        private bool FishNearby()
        {
            var nearest = float.PositiveInfinity;
            for (var i = 0; i < fishRoots.Count; i++)
            {
                if (fishRoots[i] == null) continue;
                var offset = fishRoots[i].position - transform.position;
                offset.y *= 0.55f;
                nearest = Mathf.Min(nearest, offset.magnitude);
            }
            return nearest < HideDistance;
        }

        private void EnterHide()
        {
            activity = Activity.Hide;
            activityTimer = Random.Range(1.4f, 2.6f);
            SetHidden(true);
        }

        private void SetHidden(bool hidden)
        {
            if (isHidden == hidden) return;
            isHidden = hidden;
            for (var i = 0; i < bodyRenderers.Count; i++)
            {
                if (bodyRenderers[i] != null) bodyRenderers[i].enabled = !hidden;
            }
            for (var i = 0; i < shellRenderers.Count; i++)
            {
                if (shellRenderers[i] != null) shellRenderers[i].enabled = true;
            }
        }

        private void PickRoamDestination()
        {
            if (hunger > 0.28f && forageSites.Count > 0 && Random.value < 0.72f)
            {
                var site = NearestSite(forageSites);
                if (site != null)
                {
                    destination = RandomPointNear(site.position, 0.22f);
                    destinationActivity = Activity.Forage;
                    activity = Activity.Roam;
                    return;
                }
            }

            if (shellSites.Count > 0 && Random.value < 0.18f)
            {
                var shell = NearestSite(shellSites);
                if (shell != null)
                {
                    destination = RandomPointNear(shell.position, 0.12f);
                    destinationActivity = Activity.InspectShell;
                    activity = Activity.Roam;
                    return;
                }
            }

            destination = new Vector3(
                homePosition.x + Random.Range(-PatrolRadiusX, PatrolRadiusX),
                0f,
                homePosition.z + Random.Range(-PatrolRadiusZ, PatrolRadiusZ));
            destinationActivity = Activity.Rest;
            activity = Activity.Roam;
            SetGroundHeight(ref destination);
        }

        private Transform NearestSite(List<Transform> sites)
        {
            Transform nearest = null;
            var nearestDistance = float.PositiveInfinity;
            for (var i = 0; i < sites.Count; i++)
            {
                if (sites[i] == null) continue;
                var offset = sites[i].position - transform.position;
                offset.y = 0f;
                var distance = offset.sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearest = sites[i];
                nearestDistance = distance;
            }
            return nearest;
        }

        private Vector3 RandomPointNear(Vector3 center, float radius)
        {
            var offset = Random.insideUnitCircle * radius;
            var point = new Vector3(
                Mathf.Clamp(center.x + offset.x, -4.8f, 4.8f),
                0f,
                Mathf.Clamp(center.z + offset.y, -2.2f, 2.2f));
            SetGroundHeight(ref point);
            return point;
        }

        private void CrawlTowardDestination(float deltaTime)
        {
            var flatOffset = destination - transform.position;
            flatOffset.y = 0f;
            if (flatOffset.magnitude < 0.16f)
            {
                movementAmount = 0f;
                ArriveAtDestination();
                return;
            }

            var desiredHeading = flatOffset.normalized;
            heading = Vector3.RotateTowards(heading, desiredHeading, 48f * Mathf.Deg2Rad * deltaTime, 0f).normalized;
            var desiredRotation = Quaternion.LookRotation(heading, Vector3.up) * modelAxisCorrection;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, desiredRotation, 56f * deltaTime);

            var next = Vector3.MoveTowards(transform.position, destination, CrawlSpeed * deltaTime);
            movementAmount = deltaTime > 0f
                ? Mathf.Clamp01(Vector3.Distance(transform.position, next) / (CrawlSpeed * deltaTime))
                : 0f;
            next.x = Mathf.Clamp(next.x, -4.8f, 4.8f);
            next.z = Mathf.Clamp(next.z, -2.2f, 2.2f);
            next.y = FishTankDemoController.SubstrateHeight(next.x, next.z);
            transform.position = next;
            AnimateWalkingLegs(deltaTime);
        }

        private void ArriveAtDestination()
        {
            if (destinationActivity == Activity.Forage && hunger > 0.28f)
            {
                activity = Activity.Forage;
                activityTimer = Random.Range(3.5f, 6f);
            }
            else if (destinationActivity == Activity.InspectShell)
            {
                activity = Activity.InspectShell;
                activityTimer = Random.Range(1.2f, 2.2f);
            }
            else
            {
                activity = Activity.Rest;
                activityTimer = Random.Range(1.5f, 3.5f);
            }
        }

        private void AnimateIdle(float deltaTime)
        {
            if (activity == Activity.Forage)
                hunger = Mathf.Max(0f, hunger - deltaTime * 0.11f);
            var bob = Mathf.Sin(clock * 1.8f) * 0.004f;
            var p = transform.position;
            p.y = FishTankDemoController.SubstrateHeight(p.x, p.z) + bob;
            transform.position = p;
            if (antennae != null)
            {
                // Slow sensory probing while the crab scavenges or examines a shell.
                var probe = Mathf.Sin(clock * 3.2f) * 4f;
                antennae.localRotation = antennaeRestRotation * Quaternion.Euler(probe * 0.28f, probe, -probe * 0.35f);
            }
            AnimateWalkingLegs(deltaTime);
        }

        private void AnimateWalkingLegs(float deltaTime)
        {
            if (walkingLegs.Count == 0) return;
            var stride = clock * (Mathf.PI * 2f) * 1.05f;
            var blend = 1f - Mathf.Exp(-12f * deltaTime);
            for (var i = 0; i < walkingLegs.Count; i++)
            {
                var leg = walkingLegs[i];
                if (leg.Transform == null) continue;
                var swing = Mathf.Sin(stride + leg.Phase) * 11f * movementAmount;
                var lift = Mathf.Max(0f, Mathf.Cos(stride + leg.Phase)) * 4.5f * movementAmount;
                var target = leg.RestRotation * Quaternion.Euler(lift, 0f, swing * leg.Side);
                leg.Transform.localRotation = Quaternion.Slerp(leg.Transform.localRotation, target, blend);
            }
        }

        private sealed class LegPose
        {
            public readonly Transform Transform;
            public readonly Quaternion RestRotation;
            public readonly float Phase;
            public readonly float Side;

            public LegPose(Transform transform, Quaternion restRotation, float phase, float side)
            {
                Transform = transform;
                RestRotation = restRotation;
                Phase = phase;
                Side = side;
            }
        }

        private static void SetGroundHeight(ref Vector3 point)
        {
            point.y = FishTankDemoController.SubstrateHeight(point.x, point.z);
        }

    }
}
