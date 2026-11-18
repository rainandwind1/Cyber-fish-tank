using System.Collections.Generic;
using UnityEngine;

namespace CyberFishTank.Demo
{
    public enum FishMovementState
    {
        Cruise,
        Turn,
        UTurn,
        Burst,
        Hover,
        Avoidance
    }

    /// <summary>Slowly roaming group centre. Individual fish steer around this target instead of circling one fixed point.</summary>
    public sealed class FishSchoolAnchor
    {
        public Vector3 Position { get; private set; }

        private readonly bool bottomDweller;
        private readonly int speciesIndex;
        private readonly Vector3 homePosition;
        private Vector3 destination;
        private float retargetIn;

        public FishSchoolAnchor(Vector3 start, bool bottomDweller, int speciesIndex)
        {
            Position = start;
            homePosition = start;
            this.bottomDweller = bottomDweller;
            this.speciesIndex = speciesIndex;
            PickDestination();
        }

        public void Tick(float deltaTime)
        {
            retargetIn -= deltaTime;
            if (retargetIn <= 0f || Vector3.Distance(Position, destination) < 0.3f)
                PickDestination();

            var speed = speciesIndex == 0 ? 0.78f : speciesIndex == 1 ? 0.56f : 0.38f;
            Position = Vector3.MoveTowards(Position, destination, speed * deltaTime);
        }

        private void PickDestination()
        {
            // Give each school a loose home range so independent groups do not
            // accidentally converge on the same end of the tank.
            var rangeX = speciesIndex == 0 ? 2.2f : speciesIndex == 1 ? 1.2f : 1.3f;
            var rangeZ = speciesIndex == 0 ? 0.72f : speciesIndex == 1 ? 0.48f : 0.52f;
            destination = new Vector3(
                Mathf.Clamp(homePosition.x + Random.Range(-rangeX, rangeX), -4.5f, 4.5f),
                0f,
                Mathf.Clamp(homePosition.z + Random.Range(-rangeZ, rangeZ), -1.75f, 1.75f));
            if (bottomDweller)
                destination.y = Random.Range(-2.6f, -2.05f);
            else if (speciesIndex == 0)
                destination.y = Random.Range(0.55f, 2.0f);
            else
                destination.y = Random.Range(-0.45f, 0.9f);
            retargetIn = speciesIndex == 0 ? Random.Range(6f, 10f)
                : speciesIndex == 1 ? Random.Range(7f, 11f)
                : Random.Range(5f, 8f);
        }
    }

    /// <summary>Individual movement state machine. Body orientation is kept upright; speed and tail motion follow state.</summary>
    public sealed class FishBehaviorAgent
    {
        private readonly Transform root;
        private readonly FishSchoolAnchor school;
        private readonly IReadOnlyList<Bounds> rockObstacles;
        private readonly IReadOnlyList<FishFoodPellet> foodPellets;
        private readonly int speciesIndex;
        private readonly bool bottomDweller;
        private readonly float cruiseSpeed;
        private readonly float collisionRadius;
        private readonly float swimFrequency;
        private readonly float schoolRadius;
        private readonly float cohesionWeight;
        private readonly float alignmentWeight;
        private readonly float destinationRadius;
        private readonly float verticalWander;
        private readonly float verticalSpeed;
        private readonly Vector2 hoverDuration;
        private readonly float phase;
        private readonly Quaternion modelAxisCorrection;
        private readonly List<SwimPart> swimParts = new List<SwimPart>();
        private readonly List<BodyWave> bodyWaves = new List<BodyWave>();

        private Vector3 position;
        private Vector3 heading;
        private Vector3 steeringDirection;
        private Vector3 lastFacingHeading;
        private Vector3 destination;
        private Vector3 velocity;
        private float stateTimer;
        private float destinationTimer;
        private float burstTimer;
        private float avoidanceHoldTimer;
        private float clock;
        private float smoothedYawRate;
        private float yawRateVelocity;
        private float bank;
        private float bankVelocity;
        private float swimPitch;
        private float swimPitchVelocity;
        private float satiety;
        private FishFoodPellet targetFood;
        private float foodCooldown;
        private float waterStress;

        public FishMovementState State { get; private set; } = FishMovementState.Cruise;
        public Transform Root => root;
        public float CollisionRadius => collisionRadius;
        public Vector3 Position => position;
        public int SpeciesIndex => speciesIndex;
        public bool IsSeekingFood => targetFood != null && targetFood.IsAvailable;
        public bool IsFeeding => IsSeekingFood && Vector3.Distance(position, targetFood.Position) < 0.55f;
        public float Satiety => satiety;
        public float WaterStress => waterStress;
        public string BehaviorDescription => IsFeeding ? "摄食中"
            : IsSeekingFood ? "追食中"
            : bottomDweller && position.y < -2.1f ? "贴底巡游"
            : State == FishMovementState.Avoidance ? "避让"
            : State == FishMovementState.Turn || State == FishMovementState.UTurn ? "转向"
            : State == FishMovementState.Burst ? "短促加速"
            : State == FishMovementState.Hover ? "短暂停留"
            : "群体游动";

        public void SetWaterStress(float value)
        {
            waterStress = Mathf.Clamp01(value);
        }

        public FishBehaviorAgent(Transform root, FishSchoolAnchor school, int speciesIndex,
            IReadOnlyList<Bounds> rockObstacles, IReadOnlyList<FishFoodPellet> foodPellets)
        {
            this.root = root;
            this.school = school;
            this.rockObstacles = rockObstacles;
            this.foodPellets = foodPellets;
            this.speciesIndex = speciesIndex;
            bottomDweller = speciesIndex == 2;
            satiety = Random.Range(0.38f, 0.68f);
            // The first pass was so slow that the short hover pauses read as a
            // frozen school. Keep corys calmer, but make all three groups visibly
            // cross their home ranges at aquarium scale.
            var speciesSpeed = bottomDweller ? 0.38f : speciesIndex == 1 ? 0.64f : 0.74f;
            cruiseSpeed = speciesSpeed * Random.Range(0.9f, 1.1f);
            collisionRadius = bottomDweller ? 0.48f : speciesIndex == 1 ? 0.44f : 0.46f;
            swimFrequency = bottomDweller ? Random.Range(5.4f, 6.5f)
                : speciesIndex == 1 ? Random.Range(7.2f, 8.4f)
                : Random.Range(8.6f, 10.1f);
            schoolRadius = bottomDweller ? 3.4f : speciesIndex == 1 ? 3.6f : 5.2f;
            cohesionWeight = bottomDweller ? 0.34f : speciesIndex == 1 ? 0.55f : 0.14f;
            alignmentWeight = bottomDweller ? 0.3f : speciesIndex == 1 ? 0.58f : 0.16f;
            destinationRadius = bottomDweller ? 0.72f : speciesIndex == 1 ? 1.05f : 1.7f;
            verticalWander = bottomDweller ? 0.12f : speciesIndex == 1 ? 0.2f : 0.48f;
            verticalSpeed = bottomDweller ? 0.06f : speciesIndex == 1 ? 0.1f : 0.17f;
            hoverDuration = bottomDweller ? new Vector2(0.55f, 0.95f)
                : speciesIndex == 1 ? new Vector2(0.22f, 0.42f)
                : new Vector2(0.14f, 0.32f);
            phase = Random.Range(0f, Mathf.PI * 2f);
            // Blender fish meshes have the nose along local +X, lateral axis +Y, and back +Z.
            // Map the nose to Unity's +Z forward, local +Z to world up, and lateral to world X.
            // Map Blender local +X (nose) to Unity forward +Z, local +Y to +X,
            // and Blender +Z (back) to Unity up +Y without reversing the fish.
            modelAxisCorrection = Quaternion.LookRotation(Vector3.up, Vector3.left);
            position = root.position;
            var initialDirection = Random.insideUnitCircle;
            heading = new Vector3(initialDirection.x, 0f, initialDirection.y);
            if (heading.sqrMagnitude < 0.001f) heading = Vector3.forward;
            heading.Normalize();
            steeringDirection = heading;
            lastFacingHeading = heading;
            velocity = heading * cruiseSpeed * 0.65f;
            burstTimer = Random.Range(6f, 12f);
            PickDestination();
            PrepareSwimParts();
            PrepareBodyWaves();
            UpdateFacing(0f, 0f);
        }

        public void Tick(float deltaTime, IReadOnlyList<FishBehaviorAgent> neighbors)
        {
            if (root == null) return;
            clock += deltaTime;
            satiety = Mathf.Max(0f, satiety - deltaTime / 180f);
            stateTimer -= deltaTime;
            destinationTimer -= deltaTime;
            burstTimer -= deltaTime;
            foodCooldown = Mathf.Max(0f, foodCooldown - deltaTime);
            if (targetFood == null || !targetFood.IsAvailable)
                targetFood = FindFoodTarget();

            var arrived = Vector3.Distance(position, destination) < 0.28f;
            if (arrived && State != FishMovementState.Burst && State != FishMovementState.Avoidance && State != FishMovementState.Hover)
            {
                State = FishMovementState.Hover;
                stateTimer = Random.Range(hoverDuration.x, hoverDuration.y);
            }
            else if (State == FishMovementState.Hover && stateTimer <= 0f)
            {
                PickDestination();
                State = FishMovementState.Cruise;
            }

            var avoid = CalculateSeparation(neighbors);
            var rockAvoid = CalculateRockAvoidance();
            var schooling = CalculateSchooling(neighbors);
            var avoidanceSignal = avoid.sqrMagnitude + rockAvoid.sqrMagnitude;
            if (avoidanceSignal > 0.035f)
                avoidanceHoldTimer = 0.22f;
            else
                avoidanceHoldTimer = Mathf.Max(0f, avoidanceHoldTimer - deltaTime);

            var avoiding = avoidanceHoldTimer > 0f;
            if (avoiding)
            {
                if (State != FishMovementState.Avoidance)
                {
                    State = FishMovementState.Avoidance;
                    stateTimer = 0.3f;
                }
            }
            else if (State == FishMovementState.Avoidance && stateTimer <= 0f)
            {
                State = FishMovementState.Cruise;
            }

            var outsideSchool = Vector3.Distance(position, school.Position) > 2.7f;
            // Retarget once when the timer expires. The old distance check called
            // PickDestination every frame while outside the radius, making the fish
            // chase a new random point continuously and visibly jitter.
            if (destinationTimer <= 0f && (State != FishMovementState.Hover || outsideSchool))
                PickDestination();

            var desired = Flatten(destination - position);
            if (desired.sqrMagnitude < 0.001f) desired = heading;
            var foodDirection = targetFood != null
                ? Flatten(targetFood.Position - position).normalized * 3.2f
                : Vector3.zero;
            var schoolInfluence = targetFood != null ? 0.14f : 0.42f;
            desired = (desired.normalized + schooling * schoolInfluence + foodDirection + avoid * 2.35f + rockAvoid * 3.0f);
            if (desired.sqrMagnitude < 0.001f) desired = heading;
            desired.Normalize();

            // Neighbor lists and avoidance vectors can change abruptly from one
            // frame to the next. Smooth the target direction before the body turns;
            // hazards still get a much faster response than ordinary wandering.
            var steeringRate = targetFood != null ? (avoiding ? 230f : 145f) : avoiding ? 210f : 68f;
            steeringDirection = Vector3.RotateTowards(
                steeringDirection,
                desired,
                steeringRate * Mathf.Deg2Rad * deltaTime,
                0f).normalized;
            desired = steeringDirection;

            var headingAngle = Vector3.Angle(heading, desired);
            if (State != FishMovementState.Avoidance)
            {
                if (headingAngle > 150f)
                {
                    State = FishMovementState.UTurn;
                    stateTimer = Mathf.Max(stateTimer, 0.35f);
                }
                else if (headingAngle > 52f && State != FishMovementState.UTurn)
                {
                    State = FishMovementState.Turn;
                    stateTimer = Mathf.Max(stateTimer, 0.2f);
                }
                else if ((State == FishMovementState.Turn || State == FishMovementState.UTurn) && headingAngle < 15f)
                {
                    State = FishMovementState.Cruise;
                }

                if (State == FishMovementState.Cruise && burstTimer <= 0f)
                {
                    State = FishMovementState.Burst;
                    stateTimer = Random.Range(0.75f, 1.1f);
                    burstTimer = Random.Range(7f, 14f);
                }
                else if (State == FishMovementState.Burst && stateTimer <= 0f)
                {
                    State = FishMovementState.Cruise;
                }
            }

            var turnRate = State == FishMovementState.Avoidance ? 96f
                : State == FishMovementState.UTurn ? 44f
                : targetFood != null ? 82f
                : State == FishMovementState.Turn ? 64f
                : 88f;
            heading = Vector3.RotateTowards(heading, desired, turnRate * Mathf.Deg2Rad * deltaTime, 0f).normalized;

            var speedMultiplier = State == FishMovementState.Avoidance ? 0.82f
                : State == FishMovementState.Burst ? 1.7f
                : State == FishMovementState.UTurn ? 0.42f
                : State == FishMovementState.Turn ? 0.62f
                : State == FishMovementState.Hover ? 0.48f
                : 1f;
            if (targetFood != null && State != FishMovementState.Avoidance)
                speedMultiplier = Mathf.Max(speedMultiplier, 1.28f);
            var targetVelocity = heading * cruiseSpeed * speedMultiplier * Mathf.Lerp(1f, 0.68f, waterStress);
            var turning = State == FishMovementState.Turn || State == FishMovementState.UTurn || State == FishMovementState.Avoidance;
            var acceleration = targetFood != null && State != FishMovementState.Avoidance
                ? 1.65f
                : State == FishMovementState.Burst ? 1.35f : turning ? 1.05f : 0.52f;
            var speed = Mathf.MoveTowards(velocity.magnitude, targetVelocity.magnitude, acceleration * deltaTime);
            var travelDirection = Flatten(velocity);
            if (travelDirection.sqrMagnitude < 0.0001f) travelDirection = heading;
            else travelDirection.Normalize();
            var velocityTurnRate = turning ? turnRate * 2.2f : turnRate;
            travelDirection = Vector3.RotateTowards(travelDirection, heading, velocityTurnRate * Mathf.Deg2Rad * deltaTime, 0f).normalized;
            velocity = travelDirection * speed;
            position += velocity * deltaTime;
            position += CalculateSoftSpacing(neighbors, deltaTime);
            position = ClampToTank(position);
            var targetHeight = targetFood != null ? targetFood.Position.y : destination.y;
            var climbRate = targetFood != null ? (bottomDweller ? 0.48f : 0.78f) : verticalSpeed;
            position.y = Mathf.MoveTowards(position.y, targetHeight, climbRate * deltaTime);
            if (bottomDweller) position.y = Mathf.Max(position.y, -2.58f);
            var desiredPitch = Mathf.Clamp((targetHeight - position.y) * 8f, -18f, 18f);
            swimPitch = Mathf.SmoothDamp(swimPitch, desiredPitch, ref swimPitchVelocity, 0.28f, 36f, deltaTime);

            if (targetFood != null && Vector3.Distance(position, targetFood.Position) < 0.36f)
            {
                var consumedAt = targetFood.Position;
                if (targetFood.TryConsume())
                {
                    satiety = Mathf.Min(1f, satiety + 0.3f);
                    foodCooldown = Random.Range(1.0f, 1.45f);
                    var awayFromBite = Flatten(position - consumedAt);
                    if (awayFromBite.sqrMagnitude < 0.001f) awayFromBite = heading;
                    var lateral = Vector3.Cross(Vector3.up, heading).normalized;
                    var exitDirection = (awayFromBite.normalized * 0.68f + heading * 0.24f
                        + lateral * Random.Range(-0.35f, 0.35f)).normalized;
                    destination = ClampToTank(position + exitDirection * Random.Range(0.72f, 1.05f));
                    if (bottomDweller) destination.y = position.y;
                    else destination.y = Mathf.Clamp(position.y + Random.Range(-0.18f, 0.18f), -1.8f, 1.9f);
                    destinationTimer = Random.Range(1.2f, 1.7f);
                    State = FishMovementState.Turn;
                    stateTimer = Random.Range(0.28f, 0.48f);
                }
                targetFood = null;
            }

            UpdateFacing(deltaTime, swimPitch);
            var bob = bottomDweller ? 0.008f : 0.02f;
            root.position = position + Vector3.up * Mathf.Sin(clock * 2f + phase) * bob;
            var motionSpeed = Mathf.Clamp01(velocity.magnitude / (cruiseSpeed * 1.8f));
            for (var i = 0; i < swimParts.Count; i++)
                swimParts[i].Animate(deltaTime, swimFrequency * Mathf.Lerp(0.45f, 1f, motionSpeed), phase, motionSpeed);
            for (var i = 0; i < bodyWaves.Count; i++)
                bodyWaves[i].Animate(clock, swimFrequency * Mathf.Lerp(0.45f, 1f, motionSpeed), phase, motionSpeed);
        }

        private FishFoodPellet FindFoodTarget()
        {
            if (foodCooldown > 0f || satiety > 0.82f || foodPellets == null) return null;
            FishFoodPellet nearest = null;
            var nearestDistance = bottomDweller ? 2.6f : 3.7f;
            for (var i = 0; i < foodPellets.Count; i++)
            {
                var food = foodPellets[i];
                if (food == null || !food.CanBeClaimed) continue;
                if (bottomDweller && food.Position.y > -1.35f) continue;
                var distance = Vector3.Distance(position, food.Position);
                if (distance >= nearestDistance) continue;
                nearest = food;
                nearestDistance = distance;
            }
            if (nearest != null && !nearest.TryClaim()) return FindFoodTarget();
            return nearest;
        }

        private Vector3 CalculateSeparation(IReadOnlyList<FishBehaviorAgent> neighbors)
        {
            var result = Vector3.zero;
            for (var i = 0; i < neighbors.Count; i++)
            {
                var other = neighbors[i];
                if (other == this) continue;
                var difference = position - other.position;
                difference.y *= 0.55f;
                var distance = difference.magnitude;
                var minimum = collisionRadius + other.collisionRadius + 0.18f;
                if (distance > 0.001f && distance < minimum)
                    result += difference.normalized * ((minimum - distance) / minimum);
            }
            return result;
        }

        private Vector3 CalculateSchooling(IReadOnlyList<FishBehaviorAgent> neighbors)
        {
            // Each fish reacts to a small local neighborhood, not the whole tank.
            // Three nearest same-species neighbors keep the group coherent while
            // leaving enough individuality for the school to stretch and turn.
            FishBehaviorAgent first = null, second = null, third = null;
            var d1 = float.PositiveInfinity;
            var d2 = float.PositiveInfinity;
            var d3 = float.PositiveInfinity;
            for (var i = 0; i < neighbors.Count; i++)
            {
                var other = neighbors[i];
                if (other == this || other.speciesIndex != speciesIndex) continue;
                var distance = Vector3.Distance(position, other.position);
                if (distance > schoolRadius) continue;
                if (distance < d1) { third = second; d3 = d2; second = first; d2 = d1; first = other; d1 = distance; }
                else if (distance < d2) { third = second; d3 = d2; second = other; d2 = distance; }
                else if (distance < d3) { third = other; d3 = distance; }
            }
            if (first == null) return Vector3.zero;
            var center = first.position;
            var alignment = first.heading;
            var count = 1;
            if (second != null) { center += second.position; alignment += second.heading; count++; }
            if (third != null) { center += third.position; alignment += third.heading; count++; }
            center /= count;
            alignment /= count;
            var cohesion = Flatten(center - position);
            // Let the close-range repulsion rule take over when neighbors are already packed.
            if (cohesion.magnitude < 1.2f) cohesion = Vector3.zero;
            else cohesion.Normalize();
            alignment = Flatten(alignment);
            if (alignment.sqrMagnitude > 0.001f) alignment.Normalize();
            return cohesion * cohesionWeight + alignment * alignmentWeight;
        }

        private Vector3 CalculateRockAvoidance()
        {
            var result = Vector3.zero;
            if (rockObstacles == null) return result;
            for (var i = 0; i < rockObstacles.Count; i++)
            {
                var bounds = rockObstacles[i];
                if (position.y < bounds.min.y - 0.2f || position.y > bounds.max.y + collisionRadius) continue;
                var closest = new Vector3(
                    Mathf.Clamp(position.x, bounds.min.x, bounds.max.x),
                    position.y,
                    Mathf.Clamp(position.z, bounds.min.z, bounds.max.z));
                var away = position - closest;
                away.y = 0f;
                var distance = away.magnitude;
                var clearance = collisionRadius + 0.38f;
                if (distance >= clearance) continue;
                if (distance < 0.001f)
                {
                    var centerAway = Flatten(position - bounds.center);
                    away = centerAway.sqrMagnitude > 0.001f ? centerAway.normalized : Vector3.Cross(heading, Vector3.up);
                    distance = 0f;
                }
                result += away.normalized * ((clearance - distance) / clearance);
            }
            return result;
        }

        private Vector3 CalculateSoftSpacing(IReadOnlyList<FishBehaviorAgent> neighbors, float deltaTime)
        {
            var correction = Vector3.zero;
            for (var i = 0; i < neighbors.Count; i++)
            {
                var other = neighbors[i];
                if (other == this) continue;
                var difference = position - other.position;
                difference.y *= 0.6f;
                var distance = difference.magnitude;
                var minimum = collisionRadius + other.collisionRadius + 0.12f;
                if (distance > 0.001f && distance < minimum)
                    correction += difference.normalized * (minimum - distance);
            }
            // A small speed-limited correction prevents interpenetration without
            // the frame-to-frame teleport that made the old spacing pass jitter.
            return Vector3.ClampMagnitude(correction * 2.2f, 0.32f * deltaTime);
        }

        private void PickDestination()
        {
            var forward = Flatten(heading);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();
            var lateral = Vector3.Cross(Vector3.up, forward).normalized;
            var forwardDistance = bottomDweller ? Random.Range(0.45f, 0.9f)
                : speciesIndex == 1 ? Random.Range(0.75f, 1.4f)
                : Random.Range(1.0f, 2.0f);
            var sideDistance = Random.Range(-destinationRadius * 0.7f, destinationRadius * 0.7f);
            var verticalDistance = Random.Range(-destinationRadius * verticalWander, destinationRadius * verticalWander);
            var offset = forward * forwardDistance + lateral * sideDistance + Vector3.up * verticalDistance;
            destination = ClampToTank(school.Position + offset);
            destinationTimer = bottomDweller ? Random.Range(2.7f, 4.6f)
                : speciesIndex == 1 ? Random.Range(5f, 7.5f)
                : Random.Range(4.2f, 7f);
        }

        private void UpdateFacing(float deltaTime, float pitchDegrees)
        {
            var flatHeading = Flatten(velocity.sqrMagnitude > 0.0025f ? velocity : heading);
            if (flatHeading.sqrMagnitude < 0.001f) return;
            var uprightRotation = Quaternion.LookRotation(flatHeading.normalized, Vector3.up) * modelAxisCorrection;
            if (Mathf.Abs(pitchDegrees) > 0.01f)
            {
                var pitchAxis = Vector3.Cross(Vector3.up, flatHeading.normalized);
                if (pitchAxis.sqrMagnitude > 0.001f)
                    uprightRotation = Quaternion.AngleAxis(-pitchDegrees, pitchAxis.normalized) * uprightRotation;
            }
            if (deltaTime <= 0f)
            {
                root.rotation = uprightRotation;
                lastFacingHeading = flatHeading.normalized;
                return;
            }

            var yawRate = Vector3.SignedAngle(lastFacingHeading, flatHeading.normalized, Vector3.up) / Mathf.Max(deltaTime, 0.0001f);
            smoothedYawRate = Mathf.SmoothDamp(smoothedYawRate, yawRate, ref yawRateVelocity, 0.18f, 360f, deltaTime);
            var targetBank = Mathf.Clamp(-smoothedYawRate * 0.025f, -8f, 8f);
            bank = Mathf.SmoothDamp(bank, targetBank, ref bankVelocity, 0.22f, 45f, deltaTime);
            var bankedRotation = uprightRotation * Quaternion.AngleAxis(bank, Vector3.forward);
            root.rotation = Quaternion.RotateTowards(root.rotation, bankedRotation, 115f * deltaTime);
            lastFacingHeading = flatHeading.normalized;
        }

        private void PrepareSwimParts()
        {
            var transforms = root.GetComponentsInChildren<Transform>();
            var hasTailPivot = false;
            Transform tailPivot = null;
            for (var i = 0; i < transforms.Length; i++)
            {
                if (!transforms[i].name.ToLowerInvariant().Contains("tail pivot")) continue;
                swimParts.Add(new SwimPart(transforms[i], SwimPartKind.Tail));
                hasTailPivot = true;
                tailPivot = transforms[i];
            }
            var filters = root.GetComponentsInChildren<MeshFilter>();
            for (var i = 0; i < filters.Length; i++)
            {
                if (filters[i].sharedMesh == null) continue;
                if (hasTailPivot && filters[i].transform.IsChildOf(tailPivot)) continue;
                var name = filters[i].name.ToLowerInvariant();
                var isDecorativeLine = name.Contains("ray") || name.Contains("streak") || name.Contains("patch");
                var isTail = !isDecorativeLine && (name.Contains("tail") || name.Contains("caudal fin"));
                var isPectoralFin = name.Contains("pectoral");
                var isMedianFin = name.Contains("dorsal") || name.Contains("anal");
                var isJaw = name.Contains("lower lip");
                if (!isTail && !isPectoralFin && !isMedianFin && !isJaw) continue;
                var kind = isTail ? SwimPartKind.Tail : isPectoralFin ? SwimPartKind.Pectoral : isMedianFin ? SwimPartKind.MedianFin : SwimPartKind.Jaw;
                swimParts.Add(new SwimPart(filters[i].transform, kind));
            }
        }

        private void PrepareBodyWaves()
        {
            var filters = root.GetComponentsInChildren<MeshFilter>();
            for (var i = 0; i < filters.Length; i++)
            {
                if (filters[i].sharedMesh == null || !filters[i].name.ToLowerInvariant().Contains("body")) continue;
                if (!filters[i].sharedMesh.isReadable) continue;
                var amplitude = bottomDweller ? 0.038f : speciesIndex == 1 ? 0.048f : 0.055f;
                bodyWaves.Add(new BodyWave(filters[i].mesh, amplitude));
            }
        }

        private static Vector3 Flatten(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }

        private static Vector3 ClampToTank(Vector3 point)
        {
            // Tank interior is 12 x 6 x 5. Keep the whole fish clear of each wall,
            // accounting for the body and tail extending away from the root pivot.
            point.x = Mathf.Clamp(point.x, -4.65f, 4.65f);
            point.y = Mathf.Clamp(point.y, -2.7f, 2.1f);
            point.z = Mathf.Clamp(point.z, -1.35f, 1.35f);
            return point;
        }

        private enum SwimPartKind
        {
            Tail,
            Pectoral,
            MedianFin,
            Jaw
        }

        private sealed class SwimPart
        {
            private readonly Transform transform;
            private readonly Quaternion restRotation;
            private readonly Vector3 restPosition;
            private readonly SwimPartKind kind;
            private float swimPhase;
            private float currentAngle;
            private float angleVelocity;

            public SwimPart(Transform transform, SwimPartKind kind)
            {
                this.transform = transform;
                this.kind = kind;
                restRotation = transform.localRotation;
                restPosition = transform.localPosition;
            }

            public void Animate(float deltaTime, float frequency, float phaseOffset, float motionSpeed)
            {
                swimPhase += frequency * deltaTime;
                var strength = Mathf.Lerp(0.55f, 1f, motionSpeed);
                float angle;
                Vector3 axis;
                if (kind == SwimPartKind.Tail)
                {
                    angle = Mathf.Sin(swimPhase + phaseOffset) * 11f * strength;
                    axis = Vector3.forward;
                }
                else if (kind == SwimPartKind.Pectoral)
                {
                    angle = Mathf.Sin(swimPhase * 1.45f + phaseOffset) * 8f * strength;
                    axis = Vector3.right;
                }
                else if (kind == SwimPartKind.Jaw)
                {
                    angle = 0f;
                    axis = Vector3.right;
                    transform.localPosition = restPosition + Vector3.back * (0.0025f + Mathf.Sin(swimPhase * 0.72f + phaseOffset) * 0.0035f);
                }
                else
                {
                    angle = Mathf.Sin(swimPhase + phaseOffset - 0.55f) * 3f * strength;
                    axis = Vector3.forward;
                }
                if (kind == SwimPartKind.Jaw) return;
                currentAngle = Mathf.SmoothDampAngle(currentAngle, angle, ref angleVelocity, 0.055f, 360f, deltaTime);
                transform.localRotation = restRotation * Quaternion.AngleAxis(currentAngle, axis);
            }
        }

        private sealed class BodyWave
        {
            private readonly Mesh mesh;
            private readonly Vector3[] restVertices;
            private readonly Vector3[] animatedVertices;
            private readonly float amplitude;
            private readonly float minX;
            private readonly float maxX;

            public BodyWave(Mesh mesh, float amplitude)
            {
                this.mesh = mesh;
                this.amplitude = amplitude;
                mesh.MarkDynamic();
                restVertices = mesh.vertices;
                animatedVertices = new Vector3[restVertices.Length];
                minX = mesh.bounds.min.x;
                maxX = mesh.bounds.max.x;
            }

            public void Animate(float time, float frequency, float phase, float motionSpeed)
            {
                var span = Mathf.Max(0.001f, maxX - minX);
                var scale = amplitude * Mathf.Lerp(0.32f, 1f, motionSpeed);
                for (var i = 0; i < restVertices.Length; i++)
                {
                    var vertex = restVertices[i];
                    var posterior = Mathf.Clamp01((maxX - vertex.x) / span);
                    var envelope = posterior * posterior;
                    var wave = Mathf.Sin(time * frequency - posterior * 1.65f + phase);
                    vertex.y += wave * scale * envelope;
                    vertex.z += Mathf.Sin(time * frequency - posterior * 1.65f + phase - 0.45f) * scale * 0.12f * envelope;
                    animatedVertices[i] = vertex;
                }
                mesh.vertices = animatedVertices;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
            }
        }
    }
}
