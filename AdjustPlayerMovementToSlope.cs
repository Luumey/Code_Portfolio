// If the player is going up or down a slope, adjust the horizontal movement to be along the slope's normal.
// This is done so that the character smoothly runs up and down slopes at the same velocity.
private Vector3 AdjustVelocityToSlope(Vector3 PlayersMovementVector)
{
    // Check for a slope below the player with a raycast.
    Ray RayCast = new Ray(transform.position, Vector3.down);

    if (Physics.Raycast(RayCast, out RaycastHit HitInfo, 0.2f))
    {
        // Get the rotation needed by getting the difference between the palyer's up direction and the normal of the slope.
        Quaternion slopeRotation = Quaternion.FromToRotation(Vector3.up, HitInfo.normal);
        Vector3 AdjustedMovementVector = slopeRotation * PlayersMovementVector;

        // Not requiring the grounded state makes the player character snap to the platform if they are falling down onto it.
        if (AdjustedMovementVector.y < 0 || (isGrounded && AdjustedMovementVector.y > 0))
        {
            return AdjustedMovementVector;
        }
    }
    return PlayersMovementVector;
}