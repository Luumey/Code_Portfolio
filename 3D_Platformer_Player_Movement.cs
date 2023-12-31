using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Technical")]
    [SerializeField] private Transform frogMesh;
    [SerializeField] private Transform cameraLookTransform;
    [SerializeField] private Animator animator;
    [SerializeField] private Vector3 playerVelocity = Vector3.zero; // Serialized for debugging
    [SerializeField] private bool isGrounded;
    [SerializeField] private GrapplingTongueLauncher grapplingTongueLauncher;
    [SerializeField] private GroundCheck groundCheckScript;
    public bool inputLocked = false;
    private float inputHorizontalAxisValue;
    private float inputVerticalAxisValue;
    private float horizontalWalkSpeedPercentage;
    private float verticalWalkSpeedPercentage;
    public ParticleSystem slideDustParticles;
    private CharacterController characterController;
    private bool isZoomed = false;
    private Vector3 playerPreviousFramePosition;
    private bool chargingJump = false;
    private bool jumpInput = false;
    private Vector3 externalVelocity = Vector3.zero; // Alternative velocity to movement input velocity. Blocks movement input velocity. Only affects one frame.
    private Vector3 additionalVelocity = Vector3.zero; // Like externalVelocity, but doesn't block movement input velocity. Only affects one frame.

    [Header("Movement values")]
    [SerializeField] private float movementSpeed = 6f;
    [SerializeField] private float terminalVelocity = -5f; // Has to be a negative value
    [SerializeField] private float gravity = -10f; // Has to be a negative value
    private float verticalVelocity;
    private Vector3 knockbackVelocity;
    private float wallJumpHorizontalVelocity;
    private Vector3 slingshotVelocity;

    [Header("Jumping values")]
    [SerializeField] private float gravityMultiplierPostApex = 5f;
    [SerializeField] private float maxJumpHeight = 1f;
    [SerializeField] private float maxChargeJumpHeight = 1f;
    private float chargeJumpTimerThreshold = 0.5f; // I.e. what time counts as "holding down jump button"

    private float chargeJumpTimer = 0f;
    private float jumpInputDecayTimer = 0f;
    private float jumpVelocity;
    private float chargeJumpVelocity;


    [Header("Sliding")]
    [SerializeField] private float slidingFriction; // Higher number means the slide is faster
    [SerializeField] private float slidingLength; // How many units the slide moves the character forward
    private bool slidingInput;
    private bool slidingState;
    private float slideStartVelocity;
    private float slidingVelocity;
    private float slidingCooldown;

    [Header("Wall Cling and Wall Jump")]
    [SerializeField] private bool clingingState;
    [SerializeField] private float maxAngle; // Determines how large the angle between the character's facing direction and the face of the wall can be
    [SerializeField] private float wallDetectionLength;
    [SerializeField] private float sphereCastRadius;
    [SerializeField] private float wallClingSlideSpeed; // The speed at which the player slides down the wall
    [SerializeField] private bool wallJumpState;
    [SerializeField] private float wallJumpDrag; // Has to be a negative value
    [SerializeField] private float wallJumpLength;
    [SerializeField] private float wallJumpGravity; // Has to be a negative value
    [SerializeField] private float maxWallJumpHeight;
    private float wallJumpHorizontalStartVelocity;
    private GameObject previouslyClungWall;
    private RaycastHit frontWallHit; // Stored information from the previous raycast that hit a wall
    private float wallJumpVelocity;

    [Header("Knockback")]
    public bool knockbackState;
    float horizontalKnockbackDrag = 15f;

    [Header("Reeling slingshot")]
    private bool slingshotState = false;
    private float slingshotDrag = 0.1f;


    void Start()
    {
        // SlidingFriction and wallJumpDrag multiplied by ten to keep the friction numbers small (just looks nicer)
        slidingFriction *= 10;
        wallJumpDrag *= 10;

        characterController = gameObject.GetComponent<CharacterController>();
        playerPreviousFramePosition = transform.position;

        // Calculate jump velocity for fixed jump height, first for charged jump, then uncharged
        chargeJumpVelocity = Mathf.Sqrt(-2.0f * gravity * maxChargeJumpHeight);
        jumpVelocity = Mathf.Sqrt(-2.0f * gravity * maxJumpHeight);

        // Calculate slide start velocity
        slideStartVelocity = Mathf.Sqrt(-2.0f * -slidingFriction * slidingLength);
        slidingCooldown = 1f;

        // Calculate walljump velocity for fixed jump height
        wallJumpVelocity = Mathf.Sqrt(-2.0f * wallJumpGravity * maxWallJumpHeight);

        // Calculate force for horizontal movement during wall jump
        wallJumpHorizontalStartVelocity = Mathf.Sqrt(-2.0f * wallJumpDrag * wallJumpLength);
    }

    void Update()
    {
        if (inputLocked)
        {
            chargingJump = false;
            slidingInput = false;
            jumpInput = false;
            return;
        }

        // Charge Jump
        if (Input.GetButtonDown("ChargeJump"))
        {
            chargingJump = true;
        }
        else if (Input.GetButtonUp("ChargeJump"))
        {
            chargingJump = false;
        }

        // Jump
        if (Input.GetButtonDown("Jump"))
        {
            jumpInput = true;
        }
        else if (Input.GetButtonUp("Jump"))
        {
            jumpInput = false;
        }

        // Slide
        if (Input.GetButtonDown("Slide"))
        {
            slidingInput = true;
        }
        else if (Input.GetButtonUp("Slide"))
        {
            slidingInput = false;
        }

        inputVerticalAxisValue = Input.GetAxisRaw("Vertical");
        inputHorizontalAxisValue = Input.GetAxisRaw("Horizontal");
    }

    void FixedUpdate()
    {
        // Calculate Player's velocity
        // NOTE: I don't know why this doesn't cause error when Time.timeScale = 0
        playerVelocity = (transform.position - playerPreviousFramePosition) / Time.fixedDeltaTime;
        playerPreviousFramePosition = transform.position;


        // KNOCKBACK
        // =========

        // Perform player knockback for this frame if the player is in the knockback state, is in the air, and isn't travelling directly upwards at a velocity below 9
        // The latter half is there to make sure the player regains control partway through a lava caused knockback
        if (knockbackState && !isGrounded && (Mathf.Abs(knockbackVelocity.x) != 0f && Mathf.Abs(knockbackVelocity.z) != 0f) ^ knockbackVelocity.y >= 9f)
        {
            // Bring knockbackVelocity x and z values closer to zero, and bring y down continuously, keep x and z at zero if they already are zeros, to avoid errors
            knockbackVelocity.x = knockbackVelocity.x == 0f ? 0f : knockbackVelocity.x - Mathf.Sign(knockbackVelocity.x) * horizontalKnockbackDrag * Time.fixedDeltaTime;
            knockbackVelocity.z = knockbackVelocity.z == 0f ? 0f : knockbackVelocity.z - Mathf.Sign(knockbackVelocity.z) * horizontalKnockbackDrag * Time.fixedDeltaTime;
            knockbackVelocity.y -= 30f * Time.fixedDeltaTime;

            // Add knockback only if the Player isn't still
            if (playerVelocity != Vector3.zero)
            {
                AddExternalVelocity(knockbackVelocity * Time.fixedDeltaTime);
            }

            // Make sure the player can't slide during a knockback
            slidingInput = false;
            slidingVelocity = 0f;
            slidingState = false;
            jumpInput = false;
        }
        // Exit knockbackState
        else if (knockbackState)
        {
            // Keep the player's vertical velocity to avoid a sudden stop in the air
            verticalVelocity = knockbackVelocity.y;

            knockbackState = false;
        }


        // INPUT LOCK
        // ==========

        // Prevent the Frog receiving input from the Player
        if (inputLocked)
        {
            inputVerticalAxisValue = 0f;
            inputHorizontalAxisValue = 0f;

            // Cancel jumping
            chargeJumpTimer = 0f;
            jumpInputDecayTimer = 0f;

            // Cancel sliding
            slidingVelocity = 0f;
            slidingState = false;
            slidingInput = false;
        }

        // REELING SLINGSHOT
        // =================

        if (slingshotState)
        {
            float slingshotVelocityThreshold = 0.1f; // If the velocity is smaller than this -> stop slingshot
            float slingshotVelocityMagnitude = slingshotVelocity.magnitude;
            float slingshotHorizontalVelocityMagnitude = new Vector3(slingshotVelocity.x, 0f, slingshotVelocity.z).magnitude;

            // Case: Moving airborne -> keep (drag reduced) slingshot velocity
            if (!isGrounded && slingshotHorizontalVelocityMagnitude > slingshotVelocityThreshold)
            {
                // Reduce slingshotVelocity by drag
                slingshotVelocity = Vector3.ClampMagnitude(slingshotVelocity, slingshotVelocityMagnitude - slingshotDrag * Time.fixedDeltaTime);

                // Also clamp slingshotVelocity so that it's never higher than the Player's current velocity
                slingshotVelocity = Vector3.ClampMagnitude(slingshotVelocity, playerVelocity.magnitude);

                // Add slingshot velocity to Frog's velocity
                AddAdditionalVelocity(slingshotVelocity * Time.fixedDeltaTime);
            }
            // Case: Either grounded or slingshotVelocity too small to continue slingshot -> stop slingshot
            else if (isGrounded || slingshotHorizontalVelocityMagnitude <= slingshotVelocityThreshold)
            {
                slingshotVelocity = Vector3.zero;
                slingshotState = false;
            }

            // Make sure the player can't slide during a slingshot
            slidingInput = false;
            slidingVelocity = 0f;
            slidingState = false;
        }


        // WALL CLING
        // ==========

        // If the player isn't on the ground, check if there's a wall in front of them (when falling down)
        if (!isGrounded && verticalVelocity <= 0f)
        {
            WallClingCheck();
        }
        else
        {
            if (clingingState)
            {
                grapplingTongueLauncher.inputLocked = false;
            }
            clingingState = false;
            animator.SetBool("WallClinging", false);
            previouslyClungWall = null;
        }


        // SLIDING
        // =======

        // Set the character's slidingState to true, and set its height and velocity
        if (slidingInput && !slidingState && externalVelocity == Vector3.zero && !knockbackState)
        {
            slidingState = true;
            slidingVelocity = slideStartVelocity;
            ChangeCharacterHeight(0.7f);
            animator.SetBool("Sliding", true);
            slideDustParticles.Play();
        }
        else if (!slidingInput && slidingState) // Set the character's slidingState to false, reset its height and start the cooldown
        {
            slidingState = false;
            slidingCooldown = 0.1f;
            ChangeCharacterHeight(1.5f);
            animator.SetBool("Sliding", false);
            slideDustParticles.Stop();
        }

        // Progress cooldown if grounded
        if (!slidingState && slidingCooldown > 0f && isGrounded)
        {
            slidingCooldown -= Time.fixedDeltaTime;
        }

        // Handle sliding on the ground
        if (isGrounded && slidingState && slidingVelocity >= 0f && slidingCooldown <= 0f && !knockbackState)
        {
            AddExternalVelocity(frogMesh.forward * slidingVelocity * Time.fixedDeltaTime);

            slidingVelocity -= slidingFriction * Time.fixedDeltaTime;
        }
        // Handle sliding in the air, end slide at velocity 5 instead of 0, in order to stop an abrupt pause in the air after a slide reaches its end
        else if (slidingState && slidingVelocity >= 5f && slidingCooldown <= 0f && !clingingState && !knockbackState)
        {
            verticalVelocity = verticalVelocity > 0f ? 0f : verticalVelocity + gravity * Time.fixedDeltaTime;

            AddExternalVelocity((frogMesh.forward * slidingVelocity * Time.fixedDeltaTime) + (Vector3.up * verticalVelocity * Time.fixedDeltaTime));

            slidingVelocity -= slidingFriction * Time.fixedDeltaTime;
        }
        // When not sliding / After a slide is done
        else
        {
            slidingVelocity = 0f;
            slidingState = false;
            slidingInput = false;
            if (characterController.height == 0.7f) // Make sure that the character's height is right when not sliding
            {
                ChangeCharacterHeight(1.5f);
                animator.SetBool("Sliding", false);
                slideDustParticles.Stop();
            }
        }

        // Make sure the slide dust particles don't appear in air
        if (!IsGrounded())
        {
            slideDustParticles.Stop();
        }


        // JUMP, CHARGE JUMP AND WALL JUMP
        // ===============================

        // If the player lets go of the jump button 0.2 or more seconds before hitting the ground, clear the jump command, else store the command for when the player lands 
        if (jumpInputDecayTimer >= 0.2f)
        {
            chargeJumpTimer = 0f;
            jumpInputDecayTimer = 0f;
        }

        // Progress jumpInputDecayTimer
        if (!jumpInput && !chargingJump && chargeJumpTimer > 0f)
        {
            jumpInputDecayTimer += Time.fixedDeltaTime;
        }
        else if (chargingJump || jumpInput)
        {
            // Reset jumpInputDecayTimer if player presses down jump button again
            jumpInputDecayTimer = 0f;
        }

        // Add to chargeJumpTimer if the jump button is held down
        if (chargingJump)
        {
            chargeJumpTimer += Time.fixedDeltaTime;
        }

        Vector3 movementVector = Vector3.zero;
        Vector3 movementVectorForward = Vector3.zero;
        Vector3 movementVectorRight = Vector3.zero;

        // If the player has pressed the jump button during a slide on the ground, end slide and return to normal non-externalVelocity move case
        if (jumpInput && isGrounded && slidingState)
        {
            externalVelocity = Vector3.zero;
            slidingVelocity = 0f;
            slidingState = false;
            slidingInput = false;
            ChangeCharacterHeight(1.5f);
            animator.SetBool("Sliding", false);
            slideDustParticles.Stop();
        }

        // If the player is currently in the middle of a wall jump, calculate the externalVelocity 
        if (wallJumpState)
        {
            // Cut off the externalVelocity jump before either velocity reaches 0, to avoid an abrupt stop
            if (verticalVelocity < 1f || wallJumpHorizontalVelocity < 1f)
            {
                wallJumpState = false;
                wallJumpHorizontalVelocity = 0f;
            }
            else
            {
                // Detract from both velocities over time
                wallJumpHorizontalVelocity += wallJumpDrag * Time.fixedDeltaTime;
                verticalVelocity += wallJumpGravity * Time.fixedDeltaTime;

                AddExternalVelocity((frogMesh.forward * wallJumpHorizontalVelocity * Time.fixedDeltaTime) + (Vector3.up * verticalVelocity * Time.fixedDeltaTime));
            }
        }


        // MOVEMENT
        // ========

        // Case: external velocity given -> don't calculate velocity from movement or gravity
        if (externalVelocity != Vector3.zero)
        {
            movementVector = externalVelocity * Time.fixedDeltaTime;

            // Reset the external velocity (it should only affect this frame)
            externalVelocity = Vector3.zero;
        }
        // Case: No external velocities given
        else
        {
            // If the player has reached the apex of their jump, add a multiplier to the gravity.
            // Also apply constant force downward when grounded, in case of faulty positive isGrounded
            if (clingingState)
            {
                // If the player is clinging to a wall, drag them downwards at a slower pace
                verticalVelocity += wallClingSlideSpeed * Time.fixedDeltaTime;
            }
            else
            {
                verticalVelocity = isGrounded ?
                    -5f :
                    verticalVelocity + gravity * (playerVelocity.y < 0f ? gravityMultiplierPostApex : 1) * Time.fixedDeltaTime;
            }

            // Jump if the jump button is let go of and there is any amount of charge
            if (jumpInput && (isGrounded || clingingState))
            {
                // Case: Wall jump
                if (clingingState)
                {
                    // Set velocities for the wall jump
                    verticalVelocity = wallJumpVelocity;
                    wallJumpHorizontalVelocity = wallJumpHorizontalStartVelocity;

                    // Turn the player character away from the wall they're jumping from
                    Vector3 newPos = frogMesh.position + frontWallHit.normal;
                    frogMesh.LookAt(new Vector3(newPos.x, frogMesh.transform.position.y, newPos.z));

                    AddExternalVelocity((frogMesh.forward * wallJumpHorizontalVelocity * Time.fixedDeltaTime) + (Vector3.up * verticalVelocity * Time.fixedDeltaTime));

                    wallJumpState = true;
                    clingingState = false;
                    animator.SetBool("WallClinging", false);
                    grapplingTongueLauncher.inputLocked = false;
                    chargeJumpTimer = 0f;
                    jumpInput = false;

                    // Reverse movement after wall jump
                    horizontalWalkSpeedPercentage *= -1f;
                    verticalWalkSpeedPercentage *= -1f;

                    // Animate Jump
                    animator.SetTrigger("Jump");
                }
                // Case: normal or charge jump
                else
                {
                    // Depending on charge amount, decide which jump to do
                    verticalVelocity = chargeJumpTimer >= chargeJumpTimerThreshold ? chargeJumpVelocity : jumpVelocity;

                    // Animate Jump
                    animator.SetTrigger("Jump");

                    chargeJumpTimer = 0f;
                    jumpInput = false;
                }
            }

            // Keep character constrained in terminal velocity
            if (verticalVelocity < terminalVelocity)
            {
                verticalVelocity = terminalVelocity;
            }

            // Calculate player's vertical movement into a Vector3
            Vector3 verticalMovementVector = Vector3.up * verticalVelocity * Time.fixedDeltaTime;

            // Forward / backward input
            if (inputVerticalAxisValue != 0f)
            {
                // If there's input, add speed to a max of the input's magnitude
                verticalWalkSpeedPercentage = Mathf.Clamp(verticalWalkSpeedPercentage + inputVerticalAxisValue * 5f * Time.fixedDeltaTime, -Mathf.Abs(inputVerticalAxisValue), Mathf.Abs(inputVerticalAxisValue));
            }
            else
            {
                // Decrease speed if vertical input is 0f
                verticalWalkSpeedPercentage = Mathf.Clamp01(Mathf.Abs(verticalWalkSpeedPercentage) - 10f * Time.fixedDeltaTime) * Mathf.Sign(verticalWalkSpeedPercentage);
            }

            movementVectorForward = verticalWalkSpeedPercentage * cameraLookTransform.forward * movementSpeed * Time.fixedDeltaTime;

            // Right / left input
            if (inputHorizontalAxisValue != 0f)
            {
                // If there's input, add speed to a max of the input's magnitude
                horizontalWalkSpeedPercentage = Mathf.Clamp(horizontalWalkSpeedPercentage + inputHorizontalAxisValue * 5f * Time.fixedDeltaTime, -Mathf.Abs(inputHorizontalAxisValue), Mathf.Abs(inputHorizontalAxisValue));
            }
            else
            {
                // Decrease speed if horizontal input is 0f
                horizontalWalkSpeedPercentage = Mathf.Clamp01(Mathf.Abs(horizontalWalkSpeedPercentage) - 10f * Time.fixedDeltaTime) * Mathf.Sign(horizontalWalkSpeedPercentage);
            }

            movementVectorRight = horizontalWalkSpeedPercentage * cameraLookTransform.right * movementSpeed * Time.fixedDeltaTime;

            // Construct the movementVector from inputs
            movementVector = Vector3.ClampMagnitude(movementVectorForward + movementVectorRight, movementSpeed * Time.fixedDeltaTime);

            // Adjust velocity to slope
            movementVector = AdjustVelocityToSlope(movementVector);

            // Lock player's movement to be towards or away from the wall they are clinging to
            if (clingingState)
            {
                movementVector = LockMovementWhileClinging(movementVector);
            }

            // Add vertical velocity to movementVector
            movementVector += verticalMovementVector;
        }

        // Move
        characterController.Move(movementVector + additionalVelocity);

        // Clear additionalVelocity every frame
        additionalVelocity = Vector3.zero;


        // MESH ROTATION
        // =============

        // Force the frogMesh to look at the clung-to wall during a cling
        if (clingingState)
        {
            // Turn the player character away from the wall they're jumping from
            Vector3 newPos = frogMesh.position + -frontWallHit.normal;
            frogMesh.LookAt(new Vector3(newPos.x, frogMesh.transform.position.y, newPos.z));
        }
        else
        {
            // Case: zoomed -> use camera's rotation for the character's mesh
            if (isZoomed)
            {
                frogMesh.rotation = cameraLookTransform.rotation;
            }
            // Case: NOT zoomed -> Rotate the character's mesh towards movement input's direction
            else if (!wallJumpState)
            {
                frogMesh.LookAt(frogMesh.position + movementVectorForward + movementVectorRight);
            }
        }

        // ANIMATION
        // =========

        // Animate idle / run
        /*
            The animationMovementSpeed is usually between 0.12 and 0.16 when running,
            so it needs to be multiplied by roughly 10 to reach required values for the animation: 0 = idle, 1 = run
            Tested, and 10 seems to be just fine.
        */
        float animationMovementSpeed = (movementVectorForward + movementVectorRight).magnitude * 10f;
        animator.SetFloat("MovementSpeed", animationMovementSpeed);
    }

    public void SetGroundedState(bool newState)
    {
        isGrounded = newState;
        animator.SetBool("Airborne", !newState);
    }

    // To match the platform's velocity and rotation
    public void ParentToPlatform(Transform newParent)
    {
        transform.SetParent(newParent, true);
    }

    public void ClearParent()
    {
        transform.SetParent(null, true);
    }

    public void SetIsZoomed(bool newZoomedState)
    {
        isZoomed = newZoomedState;
    }

    public void AddExternalVelocity(Vector3 additionalExternalVelocity)
    {
        // To make pausing work:
        // Divide by fixedDeltaTime here, and when calling this function multiply the external speed by fixedDeltaTime
        // NOTE: I don't know why this doesn't cause error when Time.timeScale = 0
        externalVelocity += additionalExternalVelocity / Time.fixedDeltaTime;
    }

    public void AddAdditionalVelocity(Vector3 _additionalVelocity)
    {
        additionalVelocity += _additionalVelocity / Time.fixedDeltaTime;
    }

    public bool IsGrounded()
    {
        return isGrounded;
    }

    public Vector3 GetPlayerVelocity()
    {
        return playerVelocity;
    }

    // If the player is going up or down a slope, adjust the horizontal movement to be along the slope's normal
    private Vector3 AdjustVelocityToSlope(Vector3 velocity)
    {
        Ray ray = new Ray(transform.position, Vector3.down);

        if (Physics.Raycast(ray, out RaycastHit hitInfo, 0.2f))
        {
            Quaternion slopeRotation = Quaternion.FromToRotation(Vector3.up, hitInfo.normal);
            Vector3 adjustedVelocity = slopeRotation * velocity;

            if (adjustedVelocity.y < 0 || (isGrounded && adjustedVelocity.y > 0))
            {
                return adjustedVelocity;
            }
        }
        return velocity;
    }

    public void ResetPlayerMovement()
    {
        ClearParent();

        playerPreviousFramePosition = transform.position;
        playerVelocity = Vector3.zero;
        externalVelocity = Vector3.zero;
        slingshotVelocity = Vector3.zero;
        knockbackVelocity = Vector3.zero;
        verticalVelocity = 0f;
        isGrounded = false;

        jumpInput = false;
        chargingJump = false;
        chargeJumpTimer = 0f;
        jumpInputDecayTimer = 0f;

        slidingVelocity = 0f;
        slidingState = false;
        slidingInput = false;

        knockbackState = false;
        slingshotState = false;

        frogMesh.localRotation = Quaternion.identity;
        groundCheckScript.ResetOverlaps();
    }

    private void ChangeCharacterHeight(float newHeight)
    {
        characterController.height = newHeight;
        characterController.center = Vector3.up * (characterController.height / 2f);
    }

    // Check if there's a clingable wall in front of the player character
    private void WallClingCheck()
    {
        if (Physics.SphereCast(frogMesh.position, sphereCastRadius, frogMesh.forward, out frontWallHit, wallDetectionLength))
        {
            // If the player is already clinging to a wall, keep clinging to it
            if (clingingState)
            {
                clingingState = true;
                animator.SetBool("WallClinging", true);
            }
            else
            {
                // Check that the player is facing the wall, and that the wall has not been clung to before
                if (Vector3.Angle(frogMesh.forward, -frontWallHit.normal) <= maxAngle && previouslyClungWall != frontWallHit.collider.gameObject && frontWallHit.collider.gameObject.CompareTag("ClingableWall"))
                {
                    clingingState = true;
                    animator.SetBool("WallClinging", true);

                    // Stop player from shooting tongue out during a wall cling
                    grapplingTongueLauncher.ResetTongueLauncher();
                    grapplingTongueLauncher.inputLocked = true;

                    // Set previouslyClungWall so you can't cling to the same wall twice in a row before hitting the ground again
                    previouslyClungWall = frontWallHit.collider.gameObject;

                    // Teleport the player character to the point where the spherecast hit the wall
                    AddExternalVelocity(frontWallHit.point - transform.position);

                    // Stop the player's ascent or descent
                    verticalVelocity = 0f;
                }
                else
                {
                    clingingState = false;
                    animator.SetBool("WallClinging", false);
                    grapplingTongueLauncher.inputLocked = false;
                }
            }
        }
        else
        {
            clingingState = false;
            animator.SetBool("WallClinging", false);
            grapplingTongueLauncher.inputLocked = false;
        }
    }

    // Get player's movement's velocity in the direction of the clingable wall's normal, and use that instead of the original velocity
    private Vector3 LockMovementWhileClinging(Vector3 velocity)
    {
        Vector3 adjustedVelocity = (Vector3.Dot(frontWallHit.normal, velocity) / (frontWallHit.normal.magnitude * frontWallHit.normal.magnitude)) * frontWallHit.normal;

        return adjustedVelocity;
    }

    // Take in knockback vector from damage source and start knockback state
    public void Knockback(Vector3 knockbackVector)
    {
        knockbackVelocity = knockbackVector;

        AddExternalVelocity(knockbackVelocity * Time.fixedDeltaTime);

        knockbackState = true;

        SetGroundedState(false);

        verticalVelocity = 0f;
    }

    public void Slingshot(Vector3 slingshotVector)
    {
        slingshotVelocity = slingshotVector;
        slingshotState = true;
    }
}
