// Check if there's a clingable wall in front of the player character.
private void WallClingCheck()
{
    if (Physics.SphereCast(PlayerMesh.position, SphereCastRadius, PlayerMesh.forward, out FrontWallHit, WallDetectionLength))
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
            if (Vector3.Angle(PlayerMesh.forward, -FrontWallHit.normal) <= MaxAngle && PreviouslyClungWall != FrontWallHit.collider.gameObject && FrontWallHit.collider.gameObject.CompareTag("ClingableWall"))
            {
                clingingState = true;
                animator.SetBool("WallClinging", true);

                // Stop player from shooting tongue out during a wall cling
                grapplingTongueLauncher.ResetTongueLauncher();
                grapplingTongueLauncher.inputLocked = true;

                // Set previouslyClungWall so you can't cling to the same wall twice in a row before hitting the ground again
                PreviouslyClungWall = FrontWallHit.collider.gameObject;

                // Teleport the player character to the point where the spherecast hit the wall
                AddExternalVelocity(FrontWallHit.point - transform.position);

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