
// The following is a snippet of code I wrote as a part of a group project, which was a third person adventure game featuring ridable dragons.
// The code was a part of a dragon's AI.
// The code's purpose is to send out spherecasts out from the dragon's head, until it finds a direction without obstacles in the way.
// If it fails to find a direction without obstacles, it chooses the direction where the obstacle was the furthest away.
// The idea for this code was originally from Sebastian Laque's video on Boids; https://www.youtube.com/watch?v=bqtqltqcQhw
// I applied the concept to my problem in this project, slightly altering it to work with c++ and Unreal engine.

// This is called during gameobject construction. It creates an array of direction vectors, which all face away from the 0,0,0 point while all being equally spaced from eachother.
void ADragonPawn::InitializeDirectionsArray() {


	float NumDirections = 200;

	float GoldenRatio = (1 + UKismetMathLibrary::Sqrt(5)) / 2;
	float AngleIncrement = PI * 2 * GoldenRatio;

	for (int i = 0; i < NumDirections; i++) {
		float t = (float)i / NumDirections;
		float Inclination = UKismetMathLibrary::Acos(1 - 2 * t);
		float Azimuth = AngleIncrement * i;

		float x = UKismetMathLibrary::Cos(Inclination);
		float y = UKismetMathLibrary::Sin(Inclination) * UKismetMathLibrary::Cos(Azimuth);
		float z = UKismetMathLibrary::Sin(Inclination) * UKismetMathLibrary::Sin(Azimuth);

		FVector NewVector = FVector(x, y, z);

		Directions.Emplace(NewVector);
	}
}


FRotator ADragonPawn::GetSafeDirection(FRotator Dir) {
	if (IsPlayerControlled()) {

		FVector DragonLocation = this->GetActorLocation();

		FVector TempDir = Directions[0];
		TempDir.Normalize();
		TempDir *= SpherecastLength;
		TempDir = GetTransform().TransformVectorNoScale(TempDir);

		FVector LineTraceEnd = (DragonLocation + TempDir);

		FCollisionQueryParams TraceParams(FName(TEXT("")), false, this);

		FHitResult Hit;

		GetWorld()->SweepSingleByChannel(
			OUT Hit,
			DragonLocation,
			LineTraceEnd,
			FQuat::Identity,
			ECollisionChannel::ECC_Visibility,
			FCollisionShape::MakeSphere(SpherecastWidth),
			TraceParams
		);

		AActor* ActorHit = Hit.GetActor();

		float FurthestObstructedDst = 0.0f;

		FVector TargetLocation;

		if (ActorHit)
		{
			for (int i = 0; i < Directions.Num(); i++)
			{

				TempDir = Directions[i];

				TempDir.Normalize();

				TempDir *= SpherecastLength;

				TempDir = GetTransform().TransformVectorNoScale(TempDir);

				LineTraceEnd = (DragonLocation + TempDir);

				GetWorld()->SweepSingleByChannel(
					OUT Hit,
					DragonLocation,
					LineTraceEnd,
					FQuat::Identity,
					ECollisionChannel::ECC_Visibility,
					FCollisionShape::MakeSphere(SpherecastWidth),
					TraceParams
				);

				ActorHit = Hit.GetActor();

				if (ActorHit)
				{
					if (Hit.Distance > FurthestObstructedDst)
					{
						FurthestObstructedDst = Hit.Distance;
						TargetLocation = LineTraceEnd;
					}
				}
				else
				{
					TargetLocation = LineTraceEnd;
					i = 999;
				}
			}
			TargetLocation -= DragonLocation;
			return TargetLocation.Rotation();
		}
		return Dir;
	}
	return Dir;
}
