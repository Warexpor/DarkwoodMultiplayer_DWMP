// REMOVED: HitscanFriendlyFirePatch duplicated the Physics.Raycast that
// spawnBullet already performs, causing HitscanImpactSyncPatch.Postfix to
// fire 3x for the same hit — delivering triple damage to the client.
// HitscanImpactSyncPatch.Postfix now handles everything (damage relay,
// blood FX, BulletImpact) in one place.
