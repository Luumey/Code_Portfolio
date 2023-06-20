
// This if statement is used to see if knockback should be applied to the player.
// I know it's not much, but I'm proud of it.

if (knockbackState && !isGrounded && (Mathf.Abs(knockbackVelocity.x) != 0f && Mathf.Abs(knockbackVelocity.z) != 0f) ^ knockbackVelocity.y >= 9f)

// It first checks if the player is in a state of knockback, and if they are in the air.
// It then checks if the player would be knocked directly up or down.
// It then checks if the player's velocity upwards is more than 9.

// The knockback is only applied to the player if the knockback will NOT knock them directly up or down, unless the velocity of the knockback is very low.