using SoftMochiPet.Core;
using SoftMochiPet.Models;
using SoftMochiPet.Services;

if (args.Contains("--feed-preview", StringComparer.Ordinal))
{
    PairFeedTests.WritePreviewTrace(Path.Combine(Environment.CurrentDirectory,".codex-temp","feed-preview"));
    return;
}

if (args.Contains("--companion-preview", StringComparer.Ordinal))
{
    CompanionLifeTests.WritePreviewTrace(Path.Combine(Environment.CurrentDirectory,
        ".codex-temp", "companion-life", "ball-trace.json"));
    return;
}

if (args.Contains("--pinch-preview", StringComparer.Ordinal))
{
    CheekPinchTests.WriteOfflinePreview(Path.Combine(Environment.CurrentDirectory,
        ".codex-temp", "pinch-preview"));
    return;
}

var tests = new (string Name, Action Run)[]
{
    ("diagnostic private fields use opaque session references", DiagnosticPrivacyTests.PrivateFieldsAreOpaqueAndCorrelatable),
    ("diagnostic text hides paths and cannot inject lines", DiagnosticPrivacyTests.FreeTextDoesNotLeakPathsOrInjectLines),
    ("voice measurements match every original WAV", VoiceLoudnessTests.IndexMatchesEveryOriginalRecording),
    ("normal and burst playback preserve gains when changing volume", VoiceLoudnessTests.MainAndBurstPlayersKeepGainWhenSliderChanges),
    ("pair feeding has no range or same-height limit", PairFeedTests.FlightHasNoRangeOrSameHeightLimit),
    ("long throws are readable and symmetric", PairFeedTests.LongThrowsAreReadableAndMirrorSymmetrically),
    ("a consumed icon finishes its catch without solo suction", PairRuntimeGateTests.ConsumedIconKeepsCatchClipWithoutGhostOrSoloReplay),
    ("companion context has distance cooldown and freshness boundaries", CompanionLifeTests.ContextHasDistanceCooldownAndFreshnessBoundaries),
    ("following waits without overtaking at every frame rate", CompanionLifeTests.FollowingWaitsAndRemainsSeparatedAtEveryFrameRate),
    ("ball contacts and transitions remain continuous across DPI", CompanionLifeTests.BallContactsAndTransitionsStayContinuousAcrossDpi),
    ("repeated kicks restart the actual production animators", CompanionLifeTests.RepeatedKicksRestartActualProductionAnimators),
    ("ambient cancellation releases ownership idempotently", CompanionLifeTests.LifeCancellationIsIdempotentAndReleasesOwnership),
    ("startup and quiet mode do not invent companion activity", CompanionLifeTests.StartupAndQuietModeDoNotInventActivity),
    ("both characters have cheek-only pinch zones", CheekPinchTests.CheeksAreTargetedWithoutStealingBodyDrag),
    ("cheek pulls nibble continuously and rebound at common frame rates", CheekPinchTests.LongPullNibbleAndReleaseSettleAtAnyFrameRate),
    ("cheek pull follows both axes without release jumps", CheekPinchTests.SmallPullPreservesPoseAndTracksBothAxes),
    ("cheek contours preserve the original body and canvas", CheekPinchTests.ProductionCheeksPreserveBodyAndCanvas),
    ("continuous cheek pulling reuses its bitmap and buffer", CheekPinchTests.ContinuousPullReusesItsBitmapAndLargeBuffer),
    ("calibrated pair artwork moves its contact anchors with it", CheekPinchTests.PairCalibrationsKeepContactsInBounds),
    ("left-edge deleted icons rebound into the other pet's mouth", PairIconKickTests.LeftEdgeIconReboundsIntoVisibleMouth),
    ("pair icon kicks reach central and right-edge icons", PairIconKickTests.CentralAndRightEdgeIconsTakeDirectShots),
    ("pair icon contacts fit real calibrated corners at multiple sizes and DPIs", PairIconKickTests.RealCalibratedContactsFitDesktopCornersAtMultipleSizesAndDpis),
    ("companion invitations accept walking but protect owned actions", PairInteractionTests.InvitationsInterruptWanderingButNotOwnedActions),
    ("pair icon meals accept stored windows without interrupting attacks", PairInteractionTests.IconMealsAcceptWalkingAndStoredWindowsButNotAttacks),
    ("pair icon meals wait for short transitions then start", PairInteractionTests.IconMealsWaitForShortTransitionsThenCanStart),
    ("shared activities respond to needs and alternate casual play", PairInteractionTests.SharedActivitiesRespondToNeedsAndDoNotRepeatPointlessly),
    ("companion timing is prompt bounded and DPI-aware", PairInteractionTests.InvitationsAndApproachHaveBoundedReadableTiming),
    ("runtime partner gate accepts walking and held results", PairRuntimeGateTests.RuntimeGateAcceptsWalkingWithAHeldControllerAndDefersLanding),
    ("pair cancellation releases wait ownership for solo fallback", PairRuntimeGateTests.CancellationClearsWaitOwnershipAndArmsSoloFallback),
    ("desktop icon copy bounds are small and monitor-safe", DesktopIconCopyCaptureTests.BoundsAreSmallFiniteAndAllowNegativeMonitorCoordinates),
    ("behavior session copies only presentation without normal migration", BehaviorControlSessionTests.FirstUseCopiesOnlyPresentationWithoutNormalMigration),
    ("behavior session persists independently of normal settings", BehaviorControlSessionTests.PersistentControlSettingsIgnoreLaterNormalChanges),
    ("behavior character stores stay in the control directory", BehaviorControlSessionTests.CharacterStoresRemainInsideControlDirectory),
    ("behavior invalid preferences preserve control defaults", BehaviorControlSessionTests.InvalidPreferencesKeepControlDefaults),
    ("behavior launch separates mode and activation", BehaviorControlLaunchTests.AppSeparatesModeAndDoesNotActivateControlledPet),
    ("behavior package has isolated launcher and output", BehaviorControlLaunchTests.BehaviorPackageUsesIsolatedLauncherAndOutput),
    ("behavior menu groups actions once and filters by character", BehaviorControlMenuTests.CatalogActionsAreGroupedOnceAndFilteredByCharacter),
    ("behavior menu availability reuses items", BehaviorControlMenuTests.AvailabilityUpdatesReuseItemsAndKeepUnsupportedActionsDisabled),
    ("behavior menu clicks emit requests without autoplay", BehaviorControlMenuTests.ClicksOnlyEmitRequestsAndAutonomyStartsDisabled),
    ("behavior status updates preserve menu controls", BehaviorControlMenuTests.StatusUpdatesDoNotEnableOrRecreateControls),
    ("behavior catalog names every direct action exactly once", BehaviorControlCatalogTests.EveryDirectActionHasOneNamedCatalogEntry),
    ("behavior catalog separates food and window actions", BehaviorControlCatalogTests.CharacterMenusSeparateFoodAndWindowMischief),
    ("behavior catalog describes target and storage requirements", BehaviorControlCatalogTests.ActionConditionsDescribeRealTargetsAndStorage),
    ("behavior animation appreciation is not a substitute for interaction", BehaviorControlCatalogTests.AnimationAppreciationNeverSubstitutesForARealBehavior),
    ("direct mischief never relaxes ordinary gates", MischiefTests.DirectMischiefControlsDoNotRelaxOrdinaryGates),
    ("direct mischief respects pause and action ownership", MischiefTests.DirectMischiefFillAndStartRespectPauseAndActionOwnership),
    ("new settings enable autonomous mischief", MischiefSettingsTests.NewSettingsEnableAutonomousMischief),
    ("stored results return only for global safety reasons", PrankStorageTests.StoredResultsReturnOnlyForGlobalSafetyReasons),
    ("park and activation preserve original window result", PrankStorageTests.ParkingAndActivationPreserveTheOriginalResult),
    ("a second park cannot overwrite stored ownership", PrankStorageTests.SecondParkCannotOverwriteAnOccupiedSlot),
    ("releasing stored ownership preserves the current attack", PrankStorageTests.ReleasingStoredOwnershipDoesNotClearTheCurrentAttack),
    ("full mischief intent waits without expiry", MischiefTests.MischiefFullIntentWaitsWithoutExpiry),
    ("mischief cadence is independent of refresh rate", MischiefTests.MischiefCadenceIsRefreshRateIndependent),
    ("a blocked burst preserves small actions and full intent", MischiefTests.MischiefBlockedBurstKeepsSmallActionsAndFullIntent),
    ("unavailable targets retry after ten seconds without loss", MischiefTests.MischiefUnavailableTargetsRetryAfterTenSecondsWithoutLoss),
    ("legacy settings enable mischief exactly once", MischiefSettingsTests.LegacySettingsEnableMischiefOnce),
    ("version five opt-out survives reload", MischiefSettingsTests.VersionFiveOptOutSurvivesReload),
    ("feature sessions never migrate real mischief preferences", MischiefSettingsTests.FeatureSessionsDoNotMigrateRealMischiefSettings),
    ("menu opening notifies the owner before other handlers", TrayMenuInteractionTests.OpeningNotifiesBeforeOtherHandlers),
    ("repeated menu open and close notifications are idempotent", TrayMenuInteractionTests.RepeatedOpenCloseIsIdempotent),
    ("cancelled or failed menu opening resets interaction state", TrayMenuInteractionTests.CancelledOrFailedOpeningResetsState),
    ("menu disposal resets once and child dropdowns remain independent", TrayMenuInteractionTests.DisposeClosesOnceAndSubmenusStayIndependent),
    ("feature demo plan is complete bounded and avoids system changes", FeatureDemoTests.PlanIsCompleteBoundedAndAvoidsSystemChanges),
    ("feature demo preserves mute without migrating real settings", FeatureDemoTests.SessionPreservesMuteAndNeverMigratesRealSettings),
    ("feature demo sessions isolate character saves and retain reports", FeatureDemoTests.SessionsIsolateBothCharacterSavesAndRetainReports),
    ("feature demo invalid preferences never enable sound", FeatureDemoTests.SessionHandlesInvalidPreferencesWithoutEnablingSound),
    ("feature demo rejects stale wrong or unmoved completion evidence", FeatureDemoTests.CompletionEvidenceRejectsStaleWrongAndUnmovedWindows),
    ("feature demo requires actual minimize and restore evidence", FeatureDemoTests.SnapshotEvidenceRequiresMinimizingAndRestoringTheSameWindow),
    ("feature meter preparation does not require a window", FeatureDemoTests.MeterPreparationDoesNotRequireAWindow),
    ("held-window demos wait for manual restore without a countdown", FeatureDemoTests.HeldWindowsWaitForManualRestoreWithoutCountdown),
    ("strike timings cover five attacks with bounded phases", PrankStrikeTimingTests.DefinitionsCoverEveryStrikeWithPositivePhases),
    ("strike timing preserves monotonic frame time and exact hit stop", PrankStrikeTimingTests.ProgressIsContinuousMonotonicAndHoldsExactContact),
    ("strike motion time excludes only the local impact pause", PrankStrikeTimingTests.MotionDeltaExcludesOnlyImpactPauseAcrossPartitions),
    ("production attack markers complete once across refresh rates", PrankStrikeTimingTests.ProductionAnimatorMarkersCompleteOnceAcrossRefreshRates),
    ("native waits consume hit stop and reaction settles once", PrankStrikeTimingTests.SystemWaitConsumesPauseAndReactionSettlesOnce),
    ("temporary tear size respects DPI and work area", TearPrankPresentationTests.TemporarySizeRespectsDpiAndWorkArea),
    ("temporary tear size preserves the user size setting", TearPrankPresentationTests.TemporarySizePreservesUserSize),
    ("tear body scale joins its held key poses continuously", TearPrankPresentationTests.BodyScaleHasContinuousTearKeyPoses),
    ("window impacts front-load movement with distinct attack strength", WindowPrankTests.WindowPrankImpactFrontLoadsMotionAndDifferentiatesAttacks),
    ("hat overlay cannot reopen during close", PrankOverlayLifecycleTests.HatCannotReopenOrCloseAgainWhileClosing),
    ("snapshot overlay close is idempotent", PrankOverlayLifecycleTests.SnapshotOverlayCloseIsIdempotent),
    ("full-meter call uses only the requested original recording", FeibiVoiceTests.BurstCallUsesOnlyTheRequestedOriginalRecording),
    ("window rejection diagnostics identify each ordinary-window rule", WindowPrankTests.WindowPrankOrdinaryRejectionsAreSpecific),
    ("window occlusion requires visible coverage evidence", WindowPrankTests.WindowPrankOcclusionRequiresVisibleCoverageEvidence),
    ("window occlusion diagnostics retain restricted capture evidence", WindowOcclusionDiagnosticTests.CaptureIdentityIsRecordedWithoutAnOcclusionBypass),
    ("window occlusion diagnostics separate title identity", WindowOcclusionDiagnosticTests.TitleIdentityIsRecordedWithoutExposingWindowText),
    ("window occlusion diagnostics trust only proven transparency", WindowOcclusionDiagnosticTests.OnlyProvenTransparencyChangesOcclusionClassification),
    ("window occlusion diagnostics never trust transparent style", WindowOcclusionDiagnosticTests.TransparentStyleAndTerrainIdentityNeverProveCoverageIsAbsent),
    ("window completion requires native acknowledgement", WindowPrankTests.WindowPrankCompletionRequiresNativeAcknowledgement),
    ("own UI preserves a safe window-prank pause", WindowPrankTests.WindowPrankOwnUiPreservesSafetyPause),
    ("feature target arguments require an explicit parent", FeatureTestTargetTests.HelperArgumentsRequireAnExplicitParentProcess),
    ("feature target launch uses argument list without shell or console", FeatureTestTargetTests.HelperLaunchUsesArgumentListAndNoShellOrConsole),
    ("feature target bounds and status text are bounded", FeatureTestTargetTests.HelperBoundsAndStepTextAreBounded),
    ("feature target preparation requires actual layer and geometry", FeatureTestTargetTests.HelperPreparationRequiresAcknowledgedLayerAndGeometry),
    ("window DPI overrides stale shell scale", WindowDpiOverridesStaleShellScale),
    ("monitor DPI supplies scale without a window", MonitorDpiSuppliesScaleWithoutWindow),
    ("stationary airborne pets recover from monitor edges", StationaryAirbornePetsRecoverFromMonitorEdges),
    ("falls below the desktop recover onto its floor", FallsBelowDesktopRecoverOntoFloor),
    ("falling selects the first crossed platform", FallingSelectsFirstCrossedPlatform),
    ("surface collision supports negative monitor coordinates", SurfaceCollisionSupportsNegativeCoordinates),
    ("spanning windows become per-monitor platform segments", SpanningWindowsBecomePerMonitorSegments),
    ("window segments require a visible top edge", WindowSegmentsRequireVisibleTopEdge),
    ("tracked support follows the segment under the pet", TrackedSupportFollowsPetSegment),
    ("needs evolve and remain clamped", NeedsEvolveAndRemainClamped),
    ("meals affect needs and remember favorite type", MealsAffectNeedsAndRememberFavoriteType),
    ("life state saves atomically and advances offline", LifeStateSavesAtomicallyAndAdvancesOffline),
    ("weighted planner honors available actions", PlannerHonorsAvailableActions),
    ("each life need changes autonomous choices", LifeNeedsChangeAutonomousChoices),
    ("hunger makes icon licking more likely", HungerMakesIconLickingMoreLikely),
    ("hunger makes in-place rolling selectable", HungerMakesRollingSelectable),
    ("hunger reactions share one cooldown", HungerReactionsShareOneCooldown),
    ("idle and patrol timing stay continuous", IdleAndPatrolTimingStayContinuous),
    ("unsupported movement actions are excluded", UnsupportedMovementActionsAreExcluded),
    ("icon licking chooses a nearby visible target", IconLickingChoosesNearbyVisibleTarget),
    ("rapid deletes enter buffet mode and expire", RapidDeletesEnterBuffetModeAndExpire),
    ("subpixel movement is refresh-rate independent", SubpixelMovementIsRefreshRateIndependent),
    ("meal queue capacity includes the active deletion", MealQueueCapacityIncludesActiveDeletion),
    ("queued deletions create waiting visuals immediately", QueuedDeletionsCreateWaitingVisualsImmediately),
    ("interrupted meals return to the front of the queue", InterruptedMealsReturnToFrontOfQueue),
    ("desktop selection anchors are consumed once", DesktopSelectionAnchorsAreConsumedOnce),
    ("fallback ghost anchors never stack", FallbackGhostAnchorsNeverStack),
    ("fast upward platform motion launches the pet", FastUpwardPlatformMotionLaunchesPet),
    ("downward platform motion releases naturally", DownwardPlatformMotionReleasesNaturally),
    ("transient platform gaps preserve support", TransientPlatformGapsPreserveSupport),
    ("fast platforms catch without rigidly tethering", FastPlatformsCatchWithoutRigidTethering),
    ("climb planner finds a reachable window side", ClimbPlannerFindsReachableWindowSide),
    ("climb planner rejects a floating unreachable window", ClimbPlannerRejectsFloatingWindow),
    ("hidden time advances needs without jumping animation", HiddenTimeAdvancesNeedsWithoutJumpingAnimation),
    ("sprite foot baseline maps to the 512 canvas", SpriteFootBaselineMapsToCanvas),
    ("animator defers non-idle artwork until requested", AnimatorDefersNonIdleArtwork),
    ("animator loads every dedicated behavior clip", AnimatorLoadsEveryDedicatedBehaviorClip),
    ("voice catalog covers every packaged recording", VoiceCatalogCoversEveryPackagedRecording),
    ("voice files cannot immediately repeat", VoiceFilesCannotImmediatelyRepeat),
    ("voice directories and catalogs remain character specific", FeibiVoiceTests.VoiceDirectoriesAndCatalogsRemainCharacterSpecific),
    ("active Feibi catalog excludes the retained fall recording", FeibiVoiceTests.ActiveFeibiCatalogExcludesTheRetainedFallRecording),
    ("all nine numbered Feibi recordings participate in daily chatter", FeibiVoiceTests.AllNineNumberedRecordingsParticipateInDailyChatter),
    ("Feibi fall and throw cues use exactly the nine numbered recordings", FeibiVoiceTests.FallAndThrowCuesUseExactlyTheNineNumberedRecordings),
    ("Feibi emotion and prank cues use appropriate named recordings", FeibiVoiceTests.FeibiEmotionAndPrankCuesUseAppropriateNamedRecordings),
    ("production Feibi recordings are complete valid original PCM", FeibiVoiceTests.ProductionFeibiRecordingsAreCompleteValidOriginalPcm),
    ("shared daily and prank files respect the same twelve second cooldown", FeibiVoiceTests.SharedDailyAndPrankFilesRespectTheSameTwelveSecondCooldown),
    ("landing voice is reserved for meaningful impacts", LandingVoiceIsReservedForMeaningfulImpacts),
    ("dialogue catalog stays varied and avoids immediate repeats", DialogueCatalogStaysVariedAndAvoidsImmediateRepeats),
    ("drag release keeps the final flick and rejects gentle motion", DragReleaseKeepsFinalFlickAndRejectsGentleMotion),
    ("autonomous walking remains draggable", AutonomousWalkingRemainsDraggable),
    ("window edges expose hop slide drop and turn", WindowEdgesExposeEveryTerrainAction),
    ("maximized support launches away from the monitor center", MaximizedSupportLaunchesAwayFromCenter),
    ("right-moving windows throw the pet to the right", RightMovingWindowsThrowRight),
    ("left-moving windows throw the pet to the left", LeftMovingWindowsThrowLeft),
    ("window sides require body-height overlap", WindowSidesRequireBodyOverlap),
    ("windows already surrounding the pet do not retrigger", SurroundingWindowsDoNotRetrigger),
    ("shallow window corners remain side impacts", ShallowWindowCornersRemainSideImpacts),
    ("side collision correction stays bounded", SideCollisionCorrectionStaysBounded),
    ("moving enclosure walls transfer their direction", MovingEnclosureWallsTransferDirection),
    ("enclosure bodies remain inertial before wall contact", EnclosureBodiesRemainInertial),
    ("enclosure floors keep the pet inside", EnclosureFloorsKeepPetInside),
    ("deletion meals escape active enclosures", DeletionMealsEscapeEnclosures),
    ("icon licking is blocked inside enclosures", IconLickingIsBlockedInsideEnclosures),
    ("QQ recording overlays are excluded from terrain", QqRecordingOverlaysAreExcludedFromTerrain),
    ("ordinary QQ windows remain valid terrain", OrdinaryQqWindowsRemainValidTerrain),
    ("full-screen background windows cannot become enclosures", FullScreenBackgroundWindowsCannotBecomeEnclosures),
    ("window prank approaches from either usable side", WindowPrankApproachChoosesEitherUsableSide),
    ("foreground ordinary windows remain valid prank targets", ForegroundOrdinaryWindowsRemainValidPrankTargets),
    ("pet size slider maps and clamps its full range", PetSizeSliderMapsAndClampsRange),
    ("rested pets can still choose a random nap", RestedPetsCanChooseRandomNap),
    ("fasting mode locks hunger and suppresses hunger actions", FastingModeLocksHungerAndSuppressesActions),
    ("progress bars use shared visibility in every mode combination", ProgressDisplayTests.ModeCombinationsKeepBothBarsInSync),
    ("progress bars restore together without overriding remaining modes", ProgressDisplayTests.ModeTransitionsRespectRemainingModes),
    ("shared progress bar preferences survive restart with either primary", ProgressDisplayTests.PreferencesSurviveRestartForEitherPrimary),
    ("quiet mode allows only standing rest and sleep", QuietModeAllowsOnlyRestAndSleep),
    ("idle name calls remain between twenty and fifty seconds", IdleNameCallsStayWithinRequestedInterval),
    ("startup registration selects the portable launcher", StartupRegistrationSelectsPortableLauncher),
    ("official product assembly resources and helper commands use renamed identity", ProductBrandingTests.ProductionAssemblyAndHelperCommandsUseOfficialName),
    ("character profiles preserve legacy assets and isolate characters", CharacterProfileTests.ProfilesPreserveLegacyAssetsAndIsolateCharacters),
    ("character settings preserve preferences and normalize IDs", CharacterProfileTests.SettingsPreserveLegacyPreferencesAndNormalizeCharacterIds),
    ("coexistence and infinite preferences persist independently of life stores", CharacterProfileTests.CoexistenceAndInfinitePreferencesPersistWithoutAffectingCharacterStores),
    ("pair dialogue has short alternating character lines", CharacterProfileTests.PairDialogueUsesBothVoicesAndShortAlternatingLines),
    ("life state remains independent for each character", CharacterProfileTests.LifeStateRemainsIndependentForEachCharacter),
    ("Feibi dialogue is complete distinct and short", CharacterProfileTests.FeibiDialogueIsCompleteDistinctAndShort),
    ("Feibi explores and hops more but sleeps when tired", CharacterProfileTests.FeibiExploresAndHopsMoreButStillSleepsWhenTired),
    ("character temperament cannot bypass modes or support", CharacterProfileTests.CharacterTemperamentCannotBypassModesOrSupport),
    ("character animation uses selective cadence", CharacterProfileTests.CharacterAnimationUsesSelectiveCadence),
    ("pair feeding waits upright before the catch", CharacterProfileTests.PairFeedUsesUprightWaitingBeforeCatch),
    ("character asset validation requires every frame", CharacterProfileTests.CharacterAssetValidationRequiresEveryFrame),
    ("character switch waits for drag and meal completion", CharacterSwitchTests.SwitchWaitsForDragAndMealCompletion),
    ("suction anchors preserve the legacy path and follow character geometry", CharacterSwitchTests.SuctionAnchorsPreserveLegacyPathAndFollowCharacterGeometry),
    ("every character frame decodes at runtime size with transparency", CharacterProductionAssetsTests.EveryCharacterFrameDecodesAtRuntimeSizeWithTransparency),
    ("every character icon decodes from its own package", CharacterProductionAssetsTests.EveryCharacterIconDecodesFromItsOwnPackage),
    ("each animator binds its own production idle artwork", CharacterProductionAssetsTests.EachAnimatorBindsItsOwnProductionIdleArtwork),
    ("Feibi prank contacts and hat masks match actual production pixels", CharacterProductionAssetsTests.FeibiPrankContactsAndHatMasksMatchActualProductionPixels),
    ("Feibi food capability and planner are isolated", MischiefTests.FeibiFoodCapabilityAndPlannerAreIsolated),
    ("mischief charges in active time and burst starts once", MischiefTests.MischiefChargesInActiveTimeAndBurstStartsOnlyOnce),
    ("mischief pauses without offline or disabled backlog", MischiefTests.MischiefPausesWithoutOfflineOrDisabledBacklog),
    ("manual play teasing and unavailable targets are bounded", MischiefTests.MischiefManualPlayTeasingAndUnavailableTargetsAreBounded),
    ("mischief state is independent and never persists an action", MischiefTests.MischiefStateIsIndependentAndNeverPersistsAnAction),
    ("mischief dialogue and animation contract are complete", MischiefTests.MischiefDialogueAndAnimationContractAreComplete),
    ("animation markers survive skipped frames and reentry", MischiefTests.MischiefAnimationMarkersSurviveSkippedFramesAndReentry),
    ("hatless interruptions keep logical names and loops", MischiefTests.MischiefHatlessInterruptionsKeepLogicalNamesAndOriginalLoops),
    ("window prank eligibility fails closed", WindowPrankTests.WindowPrankEligibilityFailsClosed),
    ("held windows require confirmed minimize and ownership", WindowPrankHoldTests.HoldingRequiresConfirmedMinimizeAndUnchangedOwnership),
    ("held windows survive unrelated foreground changes", WindowPrankHoldTests.HeldResultsSurviveUnrelatedForegroundChanges),
    ("held windows release external restores without rehiding", WindowPrankHoldTests.HeldMonitoringReleasesExternalRestoreWithoutRehiding),
    ("pet interactions keep held results but safety returns do not", WindowPrankHoldTests.PetInteractionKeepsResultsButExplicitAndSafetyReturnsDoNot),
    ("window prank motion preserves size and work area", WindowPrankTests.WindowPrankMotionPreservesSizeAndWorkArea),
    ("window prank DWM insets remain physical", WindowPrankTests.WindowPrankDwmInsetsRemainPhysical),
    ("window prank rejects black or uniform captures", WindowPrankTests.WindowPrankRejectsBlackOrUniformCaptures),
    ("window prank shards share seams and cover whole image", WindowPrankTests.WindowPrankShardsShareSeamsAndCoverWholeImage),
    ("window prank tear has one shared irregular seam", WindowPrankTests.WindowPrankTearHasOneSharedIrregularSeam),
    ("window prank restore acknowledgement is not user takeover", WindowPrankTests.WindowPrankRestoreAcknowledgementDoesNotLookLikeTakeover),
    ("window prank shatter uses actual contact", WindowPrankTests.WindowPrankShatterUsesActualContact),
    ("window prank tear hands stay attached and reassemble", WindowPrankTests.WindowPrankTearHandsStayAttachedAndReassemble),
    ("collection geometry preserves endpoints and fits the hat", WindowPrankRenderTests.CollectionGeometryPreservesEndpointsAndFitsTheHat),
    ("tear pulls complete halves without a horizontal wipe", WindowPrankRenderTests.TearPullsCompleteHalvesWithoutHorizontalWipe),
    ("tear tension keeps the whole window sealed until rupture", WindowPrankRenderTests.TearTensionKeepsTheWholeWindowSealedUntilRupture),
    ("tear grip tracks both hands and restores the source", WindowPrankRenderTests.TearGripTracksBothHandsAndRestoreReturnsToTheSource),
    ("effect pointers pass through held results and the live character", WindowPrankRenderTests.EffectPointerPassesOnlyThroughHeldOrCharacterArea),
    ("collection renders real pieces and restores original pixels", WindowPrankRenderTests.CollectionRendersRealPiecesAndReturnsTheOriginalPixels),
    ("production tear character stays above the window pieces", WindowPrankRenderTests.ActualTearCharacterRemainsVisibleAboveWindowPieces),
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

Environment.ExitCode = failures == 0 ? 0 : 1;
Console.WriteLine(failures == 0 ? $"PASS all {tests.Length} offline logic tests"
    : $"FAIL {failures} of {tests.Length} offline logic tests");

static void WindowDpiOverridesStaleShellScale()
{
    Assert(DpiScalePolicy.Resolve(windowDpi: 168, monitorDpi: 0, shellScalePercent: 140) == 175,
        "A per-monitor-aware window must use its own 175% DPI instead of a stale 140% shell scale.");
}

static void QqRecordingOverlaysAreExcludedFromTerrain()
{
    Assert(WindowTerrainEligibilityPolicy.ShouldIgnoreOverlay(
            "Chrome_WidgetWin_1",
            string.Empty,
            WindowTerrainEligibilityPolicy.WsExLayered | WindowTerrainEligibilityPolicy.WsExTopmost,
            style: 0),
        "An untitled layered QQ recording overlay must not become a moving platform.");
    Assert(WindowTerrainEligibilityPolicy.ShouldIgnoreOverlay(
            "QQScreenCaptureWnd",
            "QQ屏幕录制",
            extendedStyle: 0,
            WindowTerrainEligibilityPolicy.WsCaption),
        "A capture-specific window class must remain excluded even when it does not expose layered styles.");
    Assert(WindowTerrainEligibilityPolicy.ShouldIgnoreOverlay(
            "OverlayWindow",
            string.Empty,
            WindowTerrainEligibilityPolicy.WsExTransparent,
            style: 0),
        "Click-through overlays cannot be solid collision terrain.");
    Assert(WindowTerrainEligibilityPolicy.IsLikelyCaptureOverlay(
            "Chrome_WidgetWin_1", string.Empty,
            WindowTerrainEligibilityPolicy.WsExLayered | WindowTerrainEligibilityPolicy.WsExTopmost,
            style: 0),
        "A borderless layered capture layer should not block a visible target.");
    Assert(!WindowTerrainEligibilityPolicy.IsLikelyCaptureOverlay(
            "Chrome_WidgetWin_1", "普通窗口",
            extendedStyle: 0, WindowTerrainEligibilityPolicy.WsCaption),
        "An ordinary titled window must remain a real occluder.");
    Assert(WindowTerrainEligibilityPolicy.IsLikelyCaptureOverlay(
            "CEF-OSC-WIDGET", "NVIDIA GeForce Overlay",
            WindowTerrainEligibilityPolicy.WsExLayered,
            WindowTerrainEligibilityPolicy.WsPopup),
        "The known NVIDIA borderless overlay must not block a visible target.");
    Assert(WindowTerrainEligibilityPolicy.IsLikelyCaptureOverlay(
            "Windows.UI.Core.CoreWindow", "Windows 输入体验",
            extendedStyle: 0,
            WindowTerrainEligibilityPolicy.WsPopup),
        "The known Windows input overlay must not block a visible target.");
}

static void OrdinaryQqWindowsRemainValidTerrain()
{
    Assert(!WindowTerrainEligibilityPolicy.ShouldIgnoreOverlay(
            "Chrome_WidgetWin_1",
            "QQ",
            extendedStyle: 0x100,
            WindowTerrainEligibilityPolicy.WsCaption),
        "The normal titled QQ window should still support standing and enclosure collisions.");
    Assert(!WindowTerrainEligibilityPolicy.ShouldIgnoreOverlay(
            "Chrome_WidgetWin_1",
            "ChatGPT",
            extendedStyle: 0x100,
            WindowTerrainEligibilityPolicy.WsCaption),
        "Ordinary Chromium application windows must remain physical terrain.");
    Assert(!WindowTerrainEligibilityPolicy.ShouldIgnoreOverlay(
            "Chrome_WidgetWin_1",
            "如何使用 QQ 录屏 - 浏览器",
            extendedStyle: 0x100,
            WindowTerrainEligibilityPolicy.WsCaption),
        "A normal browser page that mentions recording must not be mistaken for the recorder overlay.");
}

static void FullScreenBackgroundWindowsCannotBecomeEnclosures()
{
    var monitors = new[]
    {
        new MonitorGeometry(new IntPtr(1), new System.Drawing.Rectangle(0, 0, 1920, 1080),
            new System.Drawing.Rectangle(0, 0, 1920, 1040), 100),
    };
    Assert(!WindowEnclosureEligibilityPolicy.IsBoundedBody(
            new System.Drawing.Rectangle(0, 0, 1920, 1080), monitors),
        "A full-screen background must not become an enclosure.");
    Assert(!WindowEnclosureEligibilityPolicy.IsBoundedBody(
            new System.Drawing.Rectangle(0, 0, 1920, 1040), monitors),
        "A maximized work-area window must not become an enclosure.");
    Assert(WindowEnclosureEligibilityPolicy.IsBoundedBody(
            new System.Drawing.Rectangle(220, 140, 1280, 760), monitors),
        "A bounded ordinary window remains eligible as an enclosure.");
}

static void WindowPrankApproachChoosesEitherUsableSide()
{
    var work = new System.Drawing.Rectangle(0, 0, 1920, 1080);
    var centered = new System.Drawing.Rectangle(600, 200, 800, 600);
    var plan = WindowPrankApproachPolicy.Choose(centered, work, 132, 1400);
    Assert(plan is { Direction: 1 }, "A centered window should use the nearer usable side.");
    var rightEdge = new System.Drawing.Rectangle(1200, 200, 720, 600);
    Assert(WindowPrankApproachPolicy.Choose(rightEdge, work, 132, 400)?.Direction == -1,
        "A right-edge window should be approached from the left.");
    var leftEdge = new System.Drawing.Rectangle(0, 200, 720, 600);
    Assert(WindowPrankApproachPolicy.Choose(leftEdge, work, 132, 150)?.Direction == 1,
        "A left-edge window should be approached from the right.");
}

static void ForegroundOrdinaryWindowsRemainValidPrankTargets()
{
    Assert(WindowPrankGeometry.IsEligible(true, false, false, true, false, true, true, false, true, true),
        "A visible ordinary foreground window may be attacked while the user works in it.");
    Assert(!WindowPrankGeometry.IsEligible(true, false, true, true, false, true, true, false, true, true),
        "Maximized targets remain rejected even when foreground targeting is allowed.");
}

static void MonitorDpiSuppliesScaleWithoutWindow()
{
    Assert(DpiScalePolicy.Resolve(windowDpi: 0, monitorDpi: 144, shellScalePercent: 140) == 150,
        "An arbitrary monitor should use effective DPI when no app window is on it yet.");
    Assert(DpiScalePolicy.Resolve(windowDpi: 0, monitorDpi: 0, shellScalePercent: 175) == 175,
        "The shell scale remains the compatibility fallback when DPI APIs are unavailable.");
    Assert(DpiScalePolicy.Resolve(windowDpi: 0, monitorDpi: 0, shellScalePercent: 0) == 100,
        "Invalid DPI inputs must fall back to 100% rather than a zero scale.");
}

static void PetSizeSliderMapsAndClampsRange()
{
    Assert(PetSizePolicy.FromPercent(100) == PetSizePolicy.DefaultSize,
        "The middle slider position must preserve the original pet size.");
    Assert(PetSizePolicy.ToPercent(PetSizePolicy.FromPercent(60)) == 60 &&
        PetSizePolicy.ToPercent(PetSizePolicy.FromPercent(200)) == 200,
        "Both visible slider endpoints must round-trip exactly.");
    Assert(PetSizePolicy.ClampPreferredSize(1) == PetSizePolicy.FromPercent(60) &&
        PetSizePolicy.ClampPreferredSize(1000) == PetSizePolicy.FromPercent(200),
        "Stored or requested sizes outside the UI range must be clamped safely.");
    Assert(PetSizePolicy.FitToWorkArea(440, 800, 600, 2) == 234,
        "A very large preferred size must fit a small high-DPI monitor without changing the preference.");
}

static void RestedPetsCanChooseRandomNap()
{
    var idleVariations = new HashSet<AutonomousAction>
    {
        AutonomousAction.Rest,
        AutonomousAction.Sleep,
    };
    var rested = new PetLifeState { Sleepiness = 20, Fullness = 24 };
    Assert(AutonomousBehaviorPlanner.Choose(rested, true, idleVariations, 0.70) == AutonomousAction.Rest,
        "Standing and blinking should remain the usual rested idle variation.");
    Assert(AutonomousBehaviorPlanner.Choose(rested, true, idleVariations, 0.90) == AutonomousAction.Sleep,
        "A rested pet must still have a smaller random chance to use the sleep idle variation.");
    Assert(LifeBehaviorPolicy.MinimumNapSeconds >= 7,
        "A randomly selected nap must stay visible instead of waking on the next frame.");
}

static void FastingModeLocksHungerAndSuppressesActions()
{
    Assert(PetModePolicy.ResolveHunger(83, fastingMode: true) == 0,
        "Fasting must clear a previously high hunger value immediately.");
    Assert(PetModePolicy.ResolveHunger(47, fastingMode: false) == 47,
        "Disabling fasting must preserve a valid life-state hunger value.");
    Assert(!PetModePolicy.ShouldShowProgressDisplay(PetCharacterProfile.Nuonuo,
            fastingMode: true, infiniteMode: false, coexistenceMode: false) &&
        PetModePolicy.ShouldShowProgressDisplay(PetCharacterProfile.Nuonuo,
            fastingMode: false, infiniteMode: false, coexistenceMode: false),
        "The hunger badge must disappear while fasting and return immediately when fasting is disabled.");
    Assert(!PetModePolicy.AllowsAutonomousAction(AutonomousAction.AskForFood, true, false) &&
        !PetModePolicy.AllowsAutonomousAction(AutonomousAction.LickIcon, true, false) &&
        !PetModePolicy.AllowsAutonomousAction(AutonomousAction.Roll, true, false),
        "Every hunger-only autonomous action must be unavailable while fasting.");
    Assert(PetModePolicy.AllowsAutonomousAction(AutonomousAction.Wander, true, false) &&
        PetModePolicy.AllowsDeletionMeal(quietMode: false),
        "Fasting must not disable ordinary movement or deletion meals.");
}

static void QuietModeAllowsOnlyRestAndSleep()
{
    var allowed = Enum.GetValues<AutonomousAction>()
        .Where(action => PetModePolicy.AllowsAutonomousAction(action, false, true))
        .ToHashSet();
    Assert(allowed.SetEquals([AutonomousAction.Rest, AutonomousAction.Sleep]),
        "Quiet mode must leave only standing idle and sleeping in the autonomous action set.");
    Assert(!PetModePolicy.AllowsDeletionMeal(quietMode: true),
        "A delete notification must not launch a roaming meal action in quiet mode.");
}

static void IdleNameCallsStayWithinRequestedInterval()
{
    Assert(IdleNameCallPolicy.NextDelay(0) == 20 && IdleNameCallPolicy.NextDelay(1) == 50,
        "The random interval endpoints must be exactly twenty and fifty seconds.");
    Assert(IdleNameCallPolicy.NextDelay(0.5) == 35 && IdleNameCallPolicy.NextDelay(double.NaN) == 35,
        "Midpoint and invalid random inputs must produce a safe interval inside the requested range.");
}

static void StartupRegistrationSelectsPortableLauncher()
{
    var root = Path.Combine(Path.GetTempPath(), $"SoftMochiPet-startup-{Guid.NewGuid():N}");
    var programDirectory = Path.Combine(root, "程序文件");
    var launcher = Path.Combine(root, "啾糯桌宠.exe");
    var legacyLauncher = Path.Combine(root, "糯糯桌宠.exe");
    var fallback = Path.Combine(programDirectory, "internal.exe");
    try
    {
        Directory.CreateDirectory(programDirectory);
        File.WriteAllBytes(launcher, []);
        File.WriteAllBytes(legacyLauncher, []);
        Assert(StartupRegistrationService.ResolveLaunchPath(programDirectory, fallback) == launcher,
            "The renamed portable package must register its new root launcher, not an old-name launcher or internal executable.");
        Assert(StartupRegistrationService.BuildRunCommand(launcher) == $"\"{launcher}\"",
            "The Windows Run command must quote the full launcher path.");

        File.Delete(launcher);
        Assert(StartupRegistrationService.ResolveLaunchPath(programDirectory, fallback) == fallback,
            "Without the new root launcher, use the current process rather than a stale old-brand launcher.");
        Assert(StartupRegistrationService.ValueName == "SoftMochiPet",
            "Renaming the product must preserve its existing Windows Run value instead of adding a duplicate entry.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void RightMovingWindowsThrowRight()
{
    var previous = new WindowBodySnapshot(new IntPtr(70), 100, 200, 420, 900);
    var current = previous with { Left = 260, Right = 580 };
    var impact = WindowSideCollisionPolicy.Resolve(
        previous,
        current,
        new PetCollisionBounds(540, 340, 720, 800),
        elapsedSeconds: 0.08,
        scale: 1.5);

    Assert(impact is { Direction: 1, CorrectionX: > 0, PetVelocityX: > 0, PetVelocityY: < 0 },
        "A right edge swept into the body must separate it and produce a rightward airborne impulse.");
}

static void LeftMovingWindowsThrowLeft()
{
    var previous = new WindowBodySnapshot(new IntPtr(71), 860, 200, 1180, 900);
    var current = previous with { Left = 650, Right = 970 };
    var impact = WindowSideCollisionPolicy.Resolve(
        previous,
        current,
        new PetCollisionBounds(700, 340, 840, 800),
        elapsedSeconds: 0.07,
        scale: 1.5);

    Assert(impact is { Direction: -1, CorrectionX: < 0, PetVelocityX: < 0, PetVelocityY: < 0 },
        "A left edge swept into the body must separate it and produce a leftward airborne impulse.");
}

static void WindowSidesRequireBodyOverlap()
{
    var previous = new WindowBodySnapshot(new IntPtr(72), 100, 820, 420, 1100);
    var current = previous with { Left = 260, Right = 580 };
    var impact = WindowSideCollisionPolicy.Resolve(
        previous,
        current,
        new PetCollisionBounds(540, 300, 720, 760),
        elapsedSeconds: 0.08,
        scale: 1.5);

    Assert(impact is null,
        "A window moving below the visible body must not collide through transparent pet pixels.");
}

static void SurroundingWindowsDoNotRetrigger()
{
    var previous = new WindowBodySnapshot(new IntPtr(73), 100, 200, 1200, 900);
    var current = previous with { Left = 120, Right = 1220 };
    var impact = WindowSideCollisionPolicy.Resolve(
        previous,
        current,
        new PetCollisionBounds(540, 340, 720, 800),
        elapsedSeconds: 0.08,
        scale: 1.5);

    Assert(impact is null,
        "An edge that already passed the pet must not keep generating impacts while the window overlaps it.");
}

static void ShallowWindowCornersRemainSideImpacts()
{
    var previous = new WindowBodySnapshot(new IntPtr(74), 100, 750, 420, 1100);
    var current = previous with { Left = 260, Right = 580 };
    var impact = WindowSideCollisionPolicy.Resolve(
        previous,
        current,
        new PetCollisionBounds(540, 300, 720, 760),
        elapsedSeconds: 0.08,
        scale: 1.5);

    Assert(impact is { Direction: 1 },
        "A visible upper-corner overlap must be classified as a side sweep, not a top landing.");
}

static void SideCollisionCorrectionStaysBounded()
{
    var previous = new WindowBodySnapshot(new IntPtr(75), 0, 200, 300, 900);
    var current = previous with { Left = 1100, Right = 1400 };
    var pet = new PetCollisionBounds(500, 300, 700, 800);
    var impact = WindowSideCollisionPolicy.Resolve(
        previous,
        current,
        pet,
        elapsedSeconds: 0.06,
        scale: 1.5);

    Assert(impact is { Direction: 1 } && impact.Value.CorrectionX <= 90.001,
        "A skipped live-drag frame must not teleport the pet by more than part of its body width.");
}

static void MovingEnclosureWallsTransferDirection()
{
    var previous = new WindowBodySnapshot(new IntPtr(80), 100, 100, 900, 900);
    var current = previous with { Left = 360, Right = 1160 };
    var step = WindowEnclosurePhysics.Resolve(
        new PetCollisionBounds(300, 300, 500, 700),
        previous,
        current,
        velocityX: 0,
        velocityY: 0,
        elapsedSeconds: 0.08,
        scale: 1);

    Assert(step.HitLeft && step.VelocityX > 0,
        "A right-moving left wall must sweep the pet toward the right.");

    previous = new WindowBodySnapshot(new IntPtr(81), 400, 100, 1200, 900);
    current = previous with { Left = 100, Right = 900 };
    step = WindowEnclosurePhysics.Resolve(
        new PetCollisionBounds(800, 300, 1000, 700),
        previous,
        current,
        velocityX: 0,
        velocityY: 0,
        elapsedSeconds: 0.08,
        scale: 1);

    Assert(step.HitRight && step.VelocityX < 0,
        "A left-moving right wall must sweep the pet toward the left.");
}

static void EnclosureBodiesRemainInertial()
{
    var previous = new WindowBodySnapshot(new IntPtr(82), 100, 100, 1100, 1000);
    var current = previous with { Left = 180, Right = 1180 };
    var step = WindowEnclosurePhysics.Resolve(
        new PetCollisionBounds(500, 250, 700, 650),
        previous,
        current,
        velocityX: 0,
        velocityY: 0,
        elapsedSeconds: 0.05,
        scale: 1);

    Assert(!step.HitHorizontal && Math.Abs(step.OffsetX) < 0.001,
        "Moving a roomy box must not glue its free body to the window.");
}

static void EnclosureFloorsKeepPetInside()
{
    var box = new WindowBodySnapshot(new IntPtr(83), 100, 100, 900, 900);
    var step = WindowEnclosurePhysics.Resolve(
        new PetCollisionBounds(400, 500, 600, 890),
        box,
        box,
        velocityX: 0,
        velocityY: 500,
        elapsedSeconds: 0.05,
        scale: 1);

    Assert(step.HitBottom && step.OffsetY <= 4 && step.VelocityY < 0,
        "A falling pet must bounce before its feet escape through the enclosure floor.");
}

static void DeletionMealsEscapeEnclosures()
{
    Assert(WindowEnclosureBehaviorPolicy.ShouldEscapeForMeal(
            isEnclosed: true,
            pendingMeals: 1,
            reactsToDeletes: true),
        "A queued deletion meal must interrupt an active window enclosure.");
    Assert(!WindowEnclosureBehaviorPolicy.ShouldEscapeForMeal(
            isEnclosed: true,
            pendingMeals: 0,
            reactsToDeletes: true),
        "An empty meal queue must not release the enclosure.");
}

static void IconLickingIsBlockedInsideEnclosures()
{
    Assert(!WindowEnclosureBehaviorPolicy.AllowsIconLick(isEnclosed: true),
        "Autonomous and manual icon licking must remain disabled inside a window enclosure.");
    Assert(WindowEnclosureBehaviorPolicy.AllowsIconLick(isEnclosed: false),
        "Icon licking should remain available after the pet leaves the enclosure.");
}

static void StationaryAirbornePetsRecoverFromMonitorEdges()
{
    var stationary = AirborneBoundaryPolicy.ResolveHorizontal(3866, 0, 0, 3840, 14);
    Assert(stationary is { Corrected: true, NextFootX: 3825, VelocityX: 0 },
        "A stationary foot beyond the right edge must be moved back inside the monitor.");

    var outward = AirborneBoundaryPolicy.ResolveHorizontal(3850, 600, 0, 3840, 14);
    Assert(outward.Corrected && outward.VelocityX < 0,
        "An outward throw must reflect inward after hitting the monitor edge.");
}

static void FallsBelowDesktopRecoverOntoFloor()
{
    WindowSurface[] surfaces =
    [
        new(new IntPtr(20), 0, 3839, 700, false),
        new(new IntPtr(30), 0, 3839, 2089, true),
    ];
    var recovery = WindowSurfaceProvider.FindDesktopRecoverySurface(
        surfaces,
        new IntPtr(30),
        3825,
        33132,
        inset: 10);
    Assert(recovery is { IsDesktopFloor: true, Top: 2089 },
        "A fall already below the work area must recover onto that monitor's desktop floor.");
    Assert(WindowSurfaceProvider.FindDesktopRecoverySurface(
            surfaces,
            new IntPtr(30),
            2000,
            2000,
            inset: 10) is null,
        "A normal fall above the floor must still use swept landing collision.");
}

static void FallingSelectsFirstCrossedPlatform()
{
    WindowSurface[] surfaces =
    [
        new(new IntPtr(1), 100, 700, 420, false),
        new(new IntPtr(2), 0, 900, 760, true),
    ];
    var landing = WindowSurfaceProvider.FindLandingSurface(surfaces, 320, 390, 450, inset: 10);
    Assert(landing?.SourceHandle == new IntPtr(1), "Expected the upper app-window platform.");
}

static void SurfaceCollisionSupportsNegativeCoordinates()
{
    WindowSurface[] surfaces =
    [
        new(new IntPtr(3), -1920, 0, 1079, true),
    ];
    var support = WindowSurfaceProvider.FindSupport(surfaces, -840, 1081, tolerance: 3);
    Assert(support is not null && support.IsDesktopFloor, "Expected support on the left-side monitor.");
}

static void SpanningWindowsBecomePerMonitorSegments()
{
    MonitorGeometry[] monitors =
    [
        new(new IntPtr(10), new(-1920, 0, 1920, 1080), new(-1920, 0, 1920, 1040), 100),
        new(new IntPtr(11), new(0, 0, 2560, 1440), new(0, 0, 2560, 1400), 150),
    ];
    var segments = WindowSurfaceProvider.CreateWindowSegments(
        new IntPtr(42),
        new System.Drawing.Rectangle(-300, 200, 800, 700),
        monitors);

    Assert(segments.Count == 2, "A window spanning two monitors should expose two real platform segments.");
    Assert(segments[0].Left == -300 && segments[0].Right == -1,
        "The negative-coordinate monitor segment should stop at its physical edge.");
    Assert(segments[1].Left == 0 && segments[1].Right == 499,
        "The primary-monitor segment should begin at its physical edge.");
}

static void WindowSegmentsRequireVisibleTopEdge()
{
    MonitorGeometry[] monitors =
    [
        new(new IntPtr(20), new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 100),
        new(new IntPtr(21), new(-1280, 240, 1280, 1024), new(-1280, 240, 1280, 984), 100),
    ];
    var segments = WindowSurfaceProvider.CreateWindowSegments(
        new IntPtr(43),
        new System.Drawing.Rectangle(-300, 180, 900, 600),
        monitors);

    Assert(segments.Count == 1 && segments[0].Left == 0,
        "A window body may overlap a lower monitor, but its off-screen top edge must not become a platform there.");
}

static void TrackedSupportFollowsPetSegment()
{
    WindowSurface[] surfaces =
    [
        new(new IntPtr(44), -500, -1, 260, false),
        new(new IntPtr(44), 0, 700, 310, false),
    ];
    var support = WindowSurfaceProvider.FindTrackedSurface(
        surfaces,
        new IntPtr(44),
        isDesktopFloor: false,
        footX: 220,
        footY: 312);

    Assert(support?.Left == 0 && support.Top == 310,
        "A spanning window handle must rebind to the physical segment beneath the pet.");
}

static void NeedsEvolveAndRemainClamped()
{
    var life = new PetLifeState { Hunger = 99, Fullness = 1, Sleepiness = 99, Curiosity = 99 };
    life.Advance(TimeSpan.FromHours(12), sleeping: false);
    Assert(life.Hunger == 100 && life.Fullness == 0 && life.Sleepiness == 100 && life.Curiosity == 100,
        "Awake needs should clamp into 0..100.");
    life.Advance(TimeSpan.FromHours(1), sleeping: true);
    Assert(life.Sleepiness < 10, "Sleeping should strongly reduce sleepiness.");
}

static void MealsAffectNeedsAndRememberFavoriteType()
{
    var life = new PetLifeState { Hunger = 70, Fullness = 20 };
    life.RegisterMeal("one.png", 10);
    life.RegisterMeal("two.png", 10);
    life.RegisterMeal("three.zip", 14);
    Assert(life.Hunger < 40 && life.Fullness > 50, "Meals should reduce hunger and increase fullness.");
    Assert(life.FavoriteFoodType == "png" && life.TotalMeals == 3, "Favorite type and count should persist.");
}

static void LifeStateSavesAtomicallyAndAdvancesOffline()
{
    var testDirectory = Path.Combine(
        Path.GetTempPath(),
        "SoftMochiPet.LogicTests",
        Guid.NewGuid().ToString("N"));
    var now = new DateTimeOffset(2026, 8, 18, 8, 0, 0, TimeSpan.Zero);

    try
    {
        var store = new PetLifeStateStore(testDirectory, () => now);
        var state = new PetLifeState
        {
            Hunger = 40,
            Fullness = 50,
            Sleepiness = 20,
            Curiosity = 30,
        };
        store.Save(state);

        Assert(File.Exists(store.StatePath), "A successful save should publish the final state file.");
        Assert(Directory.GetFiles(testDirectory, "*.tmp").Length == 0,
            "Atomic save staging files should not remain after replacement.");
        Assert(state.LastUpdatedUtc == now, "The in-memory state should retain the committed save time.");

        now = now.AddHours(2);
        var loaded = store.Load();
        Assert(Math.Abs(loaded.Hunger - 61.6) < 0.0001, "Offline time should increase hunger once.");
        Assert(Math.Abs(loaded.Fullness - 11.6) < 0.0001, "Offline time should reduce fullness once.");
        Assert(Math.Abs(loaded.Sleepiness - 35.6) < 0.0001, "Offline time should increase sleepiness once.");
        Assert(Math.Abs(loaded.Curiosity - 56.4) < 0.0001, "Offline time should increase curiosity once.");
        Assert(loaded.LastUpdatedUtc == now, "Loading should advance the offline timestamp to now.");
    }
    finally
    {
        if (Directory.Exists(testDirectory))
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }
}

static void PlannerHonorsAvailableActions()
{
    var life = new PetLifeState { Hunger = 100, Sleepiness = 100, Curiosity = 100 };
    var onlySleep = new HashSet<AutonomousAction> { AutonomousAction.Sleep };
    var chosen = AutonomousBehaviorPlanner.Choose(life, hasGroundSupport: true, onlySleep, randomUnit: 0.5);
    Assert(chosen == AutonomousAction.Sleep, "Cooldown availability must override raw need weights.");
}

static void LifeNeedsChangeAutonomousChoices()
{
    var restAndFood = new HashSet<AutonomousAction>
    {
        AutonomousAction.Rest,
        AutonomousAction.AskForFood,
    };
    var fed = new PetLifeState { Hunger = 20 };
    var hungry = new PetLifeState { Hunger = 100 };
    Assert(AutonomousBehaviorPlanner.Choose(fed, true, restAndFood, 0.9) == AutonomousAction.Rest,
        "Low hunger should not trigger a food appeal.");
    Assert(AutonomousBehaviorPlanner.Choose(hungry, true, restAndFood, 0.9) == AutonomousAction.AskForFood,
        "High hunger should make a food appeal selectable.");

    var restAndSleep = new HashSet<AutonomousAction>
    {
        AutonomousAction.Rest,
        AutonomousAction.Sleep,
    };
    var rested = new PetLifeState { Sleepiness = 20 };
    var sleepy = new PetLifeState { Sleepiness = 100 };
    Assert(AutonomousBehaviorPlanner.Choose(rested, true, restAndSleep, 0.7) == AutonomousAction.Rest,
        "Low sleepiness should usually keep the standing blink variation.");
    Assert(AutonomousBehaviorPlanner.Choose(sleepy, true, restAndSleep, 0.7) == AutonomousAction.Sleep,
        "High sleepiness should make sleep selectable.");

    var restAndExplore = new HashSet<AutonomousAction>
    {
        AutonomousAction.Rest,
        AutonomousAction.Explore,
    };
    var calm = new PetLifeState { Curiosity = 20 };
    var curious = new PetLifeState { Curiosity = 100 };
    Assert(AutonomousBehaviorPlanner.Choose(calm, true, restAndExplore, 0.6) == AutonomousAction.Rest,
        "Low curiosity should favor rest at the same random sample.");
    Assert(AutonomousBehaviorPlanner.Choose(curious, true, restAndExplore, 0.6) == AutonomousAction.Explore,
        "High curiosity should favor exploration at the same random sample.");

    var restAndWander = new HashSet<AutonomousAction>
    {
        AutonomousAction.Rest,
        AutonomousAction.Wander,
    };
    var empty = new PetLifeState { Fullness = 10, Curiosity = 0 };
    var full = new PetLifeState { Fullness = 100, Curiosity = 0 };
    Assert(AutonomousBehaviorPlanner.Choose(empty, true, restAndWander, 0.75) == AutonomousAction.Wander,
        "Low fullness should still allow wandering at the same random sample.");
    Assert(AutonomousBehaviorPlanner.Choose(full, true, restAndWander, 0.75) == AutonomousAction.Rest,
        "High fullness should increase the chance of resting.");
}

static void HungerMakesIconLickingMoreLikely()
{
    var restAndLick = new HashSet<AutonomousAction>
    {
        AutonomousAction.Rest,
        AutonomousAction.LickIcon,
    };
    var fed = new PetLifeState { Hunger = 20 };
    var hungry = new PetLifeState { Hunger = 100 };
    Assert(AutonomousBehaviorPlanner.Choose(fed, true, restAndLick, 0.7) == AutonomousAction.Rest,
        "A fed pet should usually rest at this deterministic sample.");
    Assert(AutonomousBehaviorPlanner.Choose(hungry, true, restAndLick, 0.7) == AutonomousAction.LickIcon,
        "At the same sample, high hunger should switch the choice to icon licking.");
}

static void HungerMakesRollingSelectable()
{
    var restAndRoll = new HashSet<AutonomousAction>
    {
        AutonomousAction.Rest,
        AutonomousAction.Roll,
    };
    var fed = new PetLifeState { Hunger = 30 };
    var hungry = new PetLifeState { Hunger = 100 };
    Assert(AutonomousBehaviorPlanner.Choose(fed, true, restAndRoll, 0.95) == AutonomousAction.Rest,
        "A fed pet should not perform the hungry roll.");
    Assert(AutonomousBehaviorPlanner.Choose(hungry, true, restAndRoll, 0.95) == AutonomousAction.Roll,
        "High hunger must make the in-place roll selectable.");
}

static void HungerReactionsShareOneCooldown()
{
    const double now = 100;
    var nextReactionAt = now + LifeBehaviorPolicy.HungerReactionCooldownSeconds;
    Assert(!LifeBehaviorPolicy.IsHungerReactionReady(AutonomousAction.AskForFood, now, nextReactionAt),
        "Food appeals must wait for the shared hunger-reaction cooldown.");
    Assert(!LifeBehaviorPolicy.IsHungerReactionReady(AutonomousAction.LickIcon, now, nextReactionAt),
        "Icon licking must not bypass a recent hungry roll or food appeal.");
    Assert(!LifeBehaviorPolicy.IsHungerReactionReady(AutonomousAction.Roll, now, nextReactionAt),
        "Hungry rolls must not alternate with another hunger action to repeat the same voice.");
    Assert(LifeBehaviorPolicy.IsHungerReactionReady(AutonomousAction.Wander, now, nextReactionAt),
        "The hunger-family cooldown must not freeze unrelated behavior.");
    Assert(LifeBehaviorPolicy.IsHungerReactionReady(AutonomousAction.Roll, nextReactionAt, nextReactionAt),
        "A hunger reaction should become available exactly when the shared cooldown expires.");
}

static void IdleAndPatrolTimingStayContinuous()
{
    Assert(LifeBehaviorPolicy.MaximumIdleSeconds == 5,
        "An available pet must leave idle after exactly five seconds.");
    Assert(LifeBehaviorPolicy.PatrolNeedCheckSeconds == 5,
        "A continuous patrol should check higher-priority needs without stopping every segment.");
    Assert(LifeBehaviorPolicy.RollInterruptionThreshold > LifeBehaviorPolicy.LickInterruptionThreshold,
        "Rolling should be a clearly hungry behavior, while licking can start slightly earlier.");
}

static void UnsupportedMovementActionsAreExcluded()
{
    var life = new PetLifeState { Curiosity = 100 };
    var movementOnly = new HashSet<AutonomousAction>
    {
        AutonomousAction.Wander,
        AutonomousAction.Explore,
    };
    var chosen = AutonomousBehaviorPlanner.Choose(life, hasGroundSupport: false, movementOnly, randomUnit: 0.99);
    Assert(chosen == AutonomousAction.Rest,
        "Without ground support, autonomous walking and exploration must not be selected.");
}

static void IconLickingChoosesNearbyVisibleTarget()
{
    System.Drawing.Point[] candidates =
    [
        new(105, 100),
        new(240, 100),
        new(380, 100),
        new(1800, 900),
    ];
    var selected = IconLickTargetSelector.Choose(candidates, new(100, 100), randomUnit: 0);
    Assert(selected == new System.Drawing.Point(240, 100),
        "The selector should skip an icon already under the pet and prefer a nearby visible target.");
    Assert(IconLickTargetSelector.Choose([], new(0, 0), 0.5) is null,
        "No exposed desktop icons should produce no licking target.");
}

static void RapidDeletesEnterBuffetModeAndExpire()
{
    var tracker = new DeletionBurstTracker();
    var start = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
    Assert(!tracker.Register(start), "One delete is not a buffet.");
    Assert(tracker.Register(start.AddSeconds(2)), "Two close deletes should enter buffet mode.");
    Assert(!tracker.Register(start.AddSeconds(10)), "Old deletes must expire outside the burst window.");
    Assert(tracker.Count == 1, "Only the new delete should remain after expiry.");
}

static void SubpixelMovementIsRefreshRateIndependent()
{
    double remainder = 0;
    var pixels = 0;
    for (var frame = 0; frame < 240; frame++)
    {
        pixels += SubpixelMotion.TakeWholePixels(0.25, ref remainder);
    }

    Assert(pixels == 60 && Math.Abs(remainder) < 0.000001,
        "Fractional movement should accumulate to the same one-second distance at 240 Hz.");
}

static void MealQueueCapacityIncludesActiveDeletion()
{
    Assert(MealQueuePolicy.CanAcceptDeletion(23, hasActiveDeletionMeal: false),
        "The 24th queued deletion should be accepted when no meal is active.");
    Assert(!MealQueuePolicy.CanAcceptDeletion(23, hasActiveDeletionMeal: true),
        "An active meal plus 23 queued deletions must fill the 24-item capacity.");
    Assert(!MealQueuePolicy.CanAcceptDeletion(24, hasActiveDeletionMeal: false),
        "A full queue must reject additional deletion visuals.");
}

static void QueuedDeletionsCreateWaitingVisualsImmediately()
{
    var queue = new MealVisualQueue<string, string>();
    var created = new List<string>();
    queue.Enqueue("first", meal =>
    {
        created.Add(meal);
        return $"ghost:{meal}";
    });
    queue.Enqueue("second", meal =>
    {
        created.Add(meal);
        return $"ghost:{meal}";
    });

    Assert(created.SequenceEqual(["first", "second"]) && queue.Count == 2,
        "Every deleted item should create its own waiting visual at enqueue time.");
    Assert(queue.TryDequeue(out var first) && first?.Meal == "first" && first.Visual == "ghost:first",
        "Waiting visuals should remain paired with meals in FIFO order.");

    var closed = new List<string>();
    queue.Clear(closed.Add);
    Assert(closed.SequenceEqual(["ghost:second"]) && queue.Count == 0,
        "Cancelling deletion reactions should close every still-waiting visual.");
}

static void InterruptedMealsReturnToFrontOfQueue()
{
    var queue = new MealVisualQueue<string, string>();
    queue.Enqueue("second", meal => $"ghost:{meal}");
    queue.Enqueue("third", meal => $"ghost:{meal}");

    queue.EnqueueFirst("first", "ghost:first");

    Assert(queue.TryDequeue(out var first) && first?.Meal == "first" && first.Visual == "ghost:first",
        "An interrupted active meal must resume before meals that were already waiting.");
    Assert(queue.TryDequeue(out var second) && second?.Meal == "second",
        "Requeueing the active meal must preserve the order of existing pending meals.");
    Assert(queue.TryDequeue(out var third) && third?.Meal == "third",
        "Every pending meal must survive the interruption.");
}

static void DesktopSelectionAnchorsAreConsumedOnce()
{
    var buffer = new DeletionAnchorBuffer<string>();
    var capturedAt = new DateTimeOffset(2026, 8, 19, 8, 0, 0, TimeSpan.Zero);
    buffer.Replace(["icon-a", "icon-b", "icon-c"], capturedAt);

    Assert(buffer.TryTake(capturedAt.AddSeconds(1), TimeSpan.FromSeconds(15), out var first) && first == "icon-a",
        "The first deletion should reserve the first selected desktop icon.");
    Assert(buffer.TryTake(capturedAt.AddSeconds(2), TimeSpan.FromSeconds(15), out var second) && second == "icon-b",
        "The second deletion must consume a different selected desktop icon.");
    Assert(buffer.TryTake(capturedAt.AddSeconds(3), TimeSpan.FromSeconds(15), out var third) && third == "icon-c",
        "Every selected icon should be consumed once in snapshot order.");
    Assert(!buffer.TryTake(capturedAt.AddSeconds(4), TimeSpan.FromSeconds(15), out _),
        "A consumed selection must never be silently reused for another deletion.");

    buffer.Replace(["alpha.txt", "beta.txt"], capturedAt);
    Assert(buffer.TryTakeMatching(
            anchor => anchor == "beta.txt",
            capturedAt.AddSeconds(1),
            TimeSpan.FromSeconds(15),
            out var matched) && matched == "beta.txt",
        "A batch deletion should match its display name to the original desktop position when available.");
    Assert(buffer.TryTake(capturedAt.AddSeconds(2), TimeSpan.FromSeconds(15), out var remaining) &&
        remaining == "alpha.txt",
        "Name matching must preserve every other selected icon reservation.");

    buffer.Replace(["stale"], capturedAt);
    Assert(!buffer.TryTake(capturedAt.AddSeconds(16), TimeSpan.FromSeconds(15), out _),
        "A stale desktop selection must not target a later unrelated deletion.");
}

static void FallbackGhostAnchorsNeverStack()
{
    var preferred = new System.Drawing.Point(500, 400);
    var occupied = new List<System.Drawing.Point>();
    for (var index = 0; index < 9; index++)
    {
        var chosen = GhostAnchorLayout.ChooseDistinct(preferred, occupied);
        Assert(occupied.All(point => Math.Abs(point.X - chosen.X) >= 56 || Math.Abs(point.Y - chosen.Y) >= 56),
            "Fallback deletion ghosts should occupy visibly separate slots.");
        occupied.Add(chosen);
    }

    Assert(occupied.Distinct().Count() == occupied.Count,
        "A deletion burst must never receive duplicate fallback coordinates.");
}

static void FastUpwardPlatformMotionLaunchesPet()
{
    var launch = PlatformRidePhysics.Evaluate(displacementY: -10, velocityY: -625, dpiScale: 1);
    Assert(launch.Response == PlatformMotionResponse.LaunchUpward && launch.InitialPetVelocityY < -500,
        "A fast rising window should throw the pet upward with inherited velocity.");

    var gentle = PlatformRidePhysics.Evaluate(displacementY: -2, velocityY: -120, dpiScale: 1);
    Assert(gentle.Response == PlatformMotionResponse.Follow,
        "A slowly moved window should continue carrying the pet without a launch.");
}

static void DownwardPlatformMotionReleasesNaturally()
{
    var fastDownward = PlatformRidePhysics.Evaluate(displacementY: 9, velocityY: 560, dpiScale: 1);
    Assert(fastDownward.Response == PlatformMotionResponse.ReleaseDownward &&
           fastDownward.InitialPetVelocityY == 0,
        "A window pulled down clearly should leave the pet briefly airborne instead of gluing her feet to it.");

    var scaledGentle = PlatformRidePhysics.Evaluate(displacementY: 2, velocityY: 120, dpiScale: 1.5);
    Assert(scaledGentle.Response == PlatformMotionResponse.Follow,
        "Small high-DPI platform adjustments should remain stable instead of creating constant micro-falls.");
}

static void TransientPlatformGapsPreserveSupport()
{
    Assert(PlatformRidePhysics.ShouldPreserveTransientSupport(0),
        "The first unavailable platform frame should preserve support.");
    Assert(PlatformRidePhysics.ShouldPreserveTransientSupport(0.25),
        "A short DWM enumeration gap should not make the pet fall.");
    Assert(!PlatformRidePhysics.ShouldPreserveTransientSupport(0.40),
        "A platform that remains unavailable must eventually release the pet.");
}

static void FastPlatformsCatchWithoutRigidTethering()
{
    var stillBelow = PlatformRidePhysics.ResolveAirborneContact(
        footY: 900,
        petVelocityY: -700,
        previousPlatformTop: 1100,
        currentPlatformTop: 950,
        deltaSeconds: 0.016,
        dpiScale: 1);
    Assert(!stillBelow.PushesPet && stillBelow.CorrectedFootY == 900,
        "A rising platform below the pet should not rigidly carry her through free flight.");

    var overtaking = PlatformRidePhysics.ResolveAirborneContact(
        footY: 900,
        petVelocityY: -200,
        previousPlatformTop: 1100,
        currentPlatformTop: 700,
        deltaSeconds: 0.016,
        dpiScale: 1);
    Assert(overtaking.PushesPet && overtaking.CorrectedFootY == 700 && overtaking.PetVelocityY < -200,
        "A fast platform that would pass through the pet must catch and push her upward.");

    var downward = PlatformRidePhysics.ResolveAirborneContact(
        footY: 900,
        petVelocityY: 100,
        previousPlatformTop: 700,
        currentPlatformTop: 1200,
        deltaSeconds: 0.016,
        dpiScale: 1);
    Assert(!downward.PushesPet && downward.CorrectedFootY == 900,
        "A descending platform must not pull an airborne pet downward like a rigid tether.");
}

static void ClimbPlannerFindsReachableWindowSide()
{
    var floor = new WindowSurface(new IntPtr(1), 0, 1919, 1080, true, 1080);
    WindowSurface[] surfaces =
    [
        floor,
        new WindowSurface(new IntPtr(2), 420, 1320, 180, false, 1090),
    ];
    var plan = WindowClimbPlanner.Choose(surfaces, floor, footX: 120, scale: 1, randomUnit: 0);
    var resolved = plan ?? throw new InvalidOperationException("Expected a reachable climb plan.");
    Assert(resolved.TargetHandle == new IntPtr(2),
        "A window whose side reaches the current floor should be climbable.");
    Assert(resolved.ApproachX < 420 && resolved.LandingX > 420 && resolved.Height == 900,
        "The plan should approach outside the side and finish safely on the top edge.");
}

static void ClimbPlannerRejectsFloatingWindow()
{
    var floor = new WindowSurface(new IntPtr(1), 0, 1919, 1080, true, 1080);
    WindowSurface[] surfaces =
    [
        floor,
        new WindowSurface(new IntPtr(3), 420, 1320, 180, false, 500),
    ];
    var plan = WindowClimbPlanner.Choose(surfaces, floor, footX: 500, scale: 1, randomUnit: 0);
    Assert(plan is null,
        "The pet must not climb empty air when a floating window side does not reach its support.");
}

static void HiddenTimeAdvancesNeedsWithoutJumpingAnimation()
{
    var timing = FrameTiming.FromElapsed(TimeSpan.FromMinutes(10).TotalSeconds);
    Assert(timing.SimulationSeconds == 600, "A rendering pause must retain its full monotonic duration.");
    Assert(Math.Abs(timing.AnimationSeconds - 0.05) < 0.000001,
        "Animation and physics must not jump by the hidden duration.");
    Assert(FrameTiming.FromElapsed(-2).SimulationSeconds == 0,
        "Invalid negative elapsed time must be ignored.");

    var life = new PetLifeState { Sleepiness = 70 };
    life.Advance(TimeSpan.FromSeconds(20), sleeping: true);
    Assert(life.Sleepiness is > 57 and < 59,
        "A visible short nap should noticeably reduce sleepiness.");
}

static void SpriteFootBaselineMapsToCanvas()
{
    Assert(Math.Abs(SpriteGeometry.FootCanvasY - 498.6666667) < 0.0001,
        "The processed 374/384 foot baseline must map to about y=499 on the 512 art canvas.");
}

static void AnimatorDefersNonIdleArtwork()
{
    var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "sprites", "runtime");
    var animator = new SpriteAnimator(runtimeDirectory);
    Assert(animator.LoadedAssetFolderCount == 1,
        "Only the idle sheet should be decoded before the first frame is shown.");
    Assert(animator.TotalAssetFolderCount > animator.LoadedAssetFolderCount,
        "Non-idle behavior sheets should remain available for deferred warm-up.");

    animator.Play("run");
    Assert(animator.LoadedAssetFolderCount == 2 && animator.CurrentClip == "run",
        "An interaction that beats warm-up should synchronously load only its requested sheet.");

    animator.PreloadRemainingAsync().GetAwaiter().GetResult();
    Assert(animator.LoadedAssetFolderCount == animator.TotalAssetFolderCount,
        "Background warm-up should eventually make every behavior sheet ready.");
}

static void AnimatorLoadsEveryDedicatedBehaviorClip()
{
    var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "sprites", "runtime");
    var animator = new SpriteAnimator(runtimeDirectory);
    string[] clips =
    [
        "walk", "toss", "fall", "land", "curious", "sleep_enter", "sleep", "sleep_exit",
        "hungry", "climb", "drag", "jump", "slide", "roll", "lick",
    ];
    foreach (var clip in clips)
    {
        animator.Play(clip);
        Assert(animator.CurrentClip == clip && animator.CurrentFrame is not null,
            $"Dedicated clip should load and display its first frame: {clip}");
    }

    string? finished = null;
    animator.AnimationFinished += clip => finished = clip;
    animator.Play("land");
    animator.Tick(2);
    Assert(finished == "land", "The non-looping landing clip should finish normally.");

    finished = null;
    animator.Play("lick");
    animator.Tick(2);
    Assert(finished == "lick", "The non-looping icon-lick clip should finish normally.");

    finished = null;
    animator.Play("sleep_exit");
    Assert(animator.CurrentFrameIndex == 7, "Wake-up must begin from the final lie-down transition frame.");
    animator.Tick(2);
    Assert(finished == "sleep_exit", "The reversed wake-up clip should finish at the upright pose.");

    finished = null;
    animator.Play("roll");
    animator.Tick(2);
    Assert(finished == "roll", "The non-looping hungry roll should complete exactly once.");

    finished = null;
    animator.Play("jump");
    animator.Tick(2);
    Assert(finished == "jump", "The terrain jump should hand off to the falling loop exactly once.");
}

static void VoiceCatalogCoversEveryPackagedRecording()
{
    foreach (var cue in Enum.GetValues<VoiceCue>())
    {
        Assert(VoiceCueCatalog.GetFileNames(cue).Count > 0,
            $"Every voice cue must resolve to at least one recording: {cue}");
    }

    var voiceDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "audio", "voice");
    Assert(Directory.Exists(voiceDirectory), "Published test output must contain the voice directory.");
    var packaged = Directory.GetFiles(voiceDirectory, "*.wav")
        .Select(Path.GetFileName)
        .Where(fileName => fileName is not null)
        .Cast<string>()
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var catalogued = VoiceCueCatalog.AllFileNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
    Assert(packaged.SetEquals(catalogued),
        $"Every recording must be catalogued exactly once. Packaged={packaged.Count}; Catalogued={catalogued.Count}.");
    Assert(catalogued.Count == 21, "Expected all 21 active, distinct voice recordings.");
    var airborne = VoiceCueCatalog.GetFileNames(VoiceCue.Scream);
    Assert(airborne.Count >= 7,
        "Frequent airborne reactions need a broad rotation instead of alternating between two files.");
    Assert(!airborne.Contains("糯糯_惊叫.wav", StringComparer.OrdinalIgnoreCase),
        "The excessively sharp original scream must stay out of airborne playback.");
}

static void VoiceFilesCannotImmediatelyRepeat()
{
    var startedAt = new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    var lastStarted = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase)
    {
        ["糯糯_委屈.wav"] = startedAt,
    };
    var files = new[] { "糯糯_委屈.wav", "糯糯_有点委屈.wav" };
    var fresh = VoicePlaybackPolicy.GetFreshFiles(files, lastStarted, startedAt.AddSeconds(2));
    Assert(fresh.Count == 1 && fresh[0] == "糯糯_有点委屈.wav",
        "A recently played recording must be excluded while a fresh alternative exists.");

    var afterCooldown = VoicePlaybackPolicy.GetFreshFiles(
        files,
        lastStarted,
        startedAt + VoicePlaybackPolicy.MinimumFileRepeatInterval);
    Assert(afterCooldown.Count == 2,
        "A recording may re-enter rotation when its global repeat interval expires.");
    Assert(VoicePlaybackPolicy.IsCoolingDown(
            startedAt.AddSeconds(1),
            startedAt,
            TimeSpan.FromSeconds(2.5)),
        "A landing reaction must recognize a voice that already played in the same flight event.");

    var rotated = VoicePlaybackPolicy.GetRotationCandidates(
        afterCooldown,
        previousFileForCue: "糯糯_委屈.wav",
        previousFileOverall: "糯糯_冷静.wav");
    Assert(rotated.Count == 1 && rotated[0] == "糯糯_有点委屈.wav",
        "The same scene must rotate away from its previous recording even when other voices played in between.");
}

static void LandingVoiceIsReservedForMeaningfulImpacts()
{
    Assert(!LandingVoicePolicy.ShouldReact(20, 180, "SupportLost", 1),
        "A tiny ordinary step down should stay silent.");
    Assert(!LandingVoicePolicy.ShouldReact(45, 500, "TerrainHop", 1),
        "A self-initiated short hop should not complain merely because it lands quickly.");
    Assert(LandingVoicePolicy.ShouldReact(95, 180, "SupportLost", 1),
        "A genuinely long fall should receive a landing reaction.");
    Assert(LandingVoicePolicy.ShouldReact(30, 400, "DragThrow", 1),
        "A hard user throw should react even when the vertical distance is short.");
    Assert(!LandingVoicePolicy.ShouldReact(100, 500, "DragThrow", 1.5),
        "Impact thresholds must scale with the monitor DPI.");
}

static void DialogueCatalogStaysVariedAndAvoidsImmediateRepeats()
{
    foreach (var cue in Enum.GetValues<DialogueCue>())
    {
        var lines = PetDialogueCatalog.GetLines(cue);
        Assert(lines.Count >= 4, $"Every situation needs several characterful alternatives: {cue}.");
        Assert(lines.All(line => !string.IsNullOrWhiteSpace(line)), $"Dialogue cannot be blank: {cue}.");
        Assert(lines.All(line => line.Length <= 14), $"Dialogue should stay short and instinctive: {cue}.");
    }

    var selector = new PetDialogueSelector(new Random(20260819));
    foreach (var cue in Enum.GetValues<DialogueCue>())
    {
        string? previous = null;
        for (var index = 0; index < 20; index++)
        {
            var current = selector.Choose(cue, "测试文件");
            Assert(current != previous, $"The same situation repeated the same line twice: {cue}.");
            Assert(!current.Contains("{item}", StringComparison.Ordinal),
                $"Dialogue template was not resolved: {cue}.");
            previous = current;
        }
    }

    var namedLine = selector.Choose(DialogueCue.TastedItem, "小点心.txt");
    Assert(namedLine.Contains("小点心.txt", StringComparison.Ordinal),
        "Meal dialogue should keep the real item name inside a short reaction.");
}

static void DragReleaseKeepsFinalFlickAndRejectsGentleMotion()
{
    var flick = new DragMotionTracker();
    flick.Reset(new System.Drawing.Point(0, 0), 0);
    flick.Add(new System.Drawing.Point(8, 0), 0.06);
    flick.Add(new System.Drawing.Point(24, 2), 0.10);
    flick.Add(new System.Drawing.Point(150, -54), 0.14);
    var thrown = flick.Release(new System.Drawing.Point(210, -82), 0.16, 1);
    Assert(thrown.HasInertia, "A fast final flick should transfer momentum after release.");
    Assert(thrown.VelocityX > 700 && thrown.VelocityY < -250,
        "The release vector should preserve the final flick direction.");

    var gentle = new DragMotionTracker();
    gentle.Reset(new System.Drawing.Point(0, 0), 0);
    gentle.Add(new System.Drawing.Point(8, 3), 0.05);
    var placed = gentle.Release(new System.Drawing.Point(18, 6), 0.12, 1);
    Assert(!placed.HasInertia, "Slow placement should not unexpectedly throw the pet.");
}

static void AutonomousWalkingRemainsDraggable()
{
    Assert(DragInteractionPolicy.CanStart(PetState.Running),
        "Autonomous running and patrol must yield immediately to a user drag.");
    Assert(DragInteractionPolicy.CanStart(PetState.Idle) &&
        DragInteractionPolicy.CanStart(PetState.Falling),
        "Existing idle and airborne drag behavior must remain available.");
    Assert(!DragInteractionPolicy.CanStart(PetState.Chomping) &&
        !DragInteractionPolicy.CanStart(PetState.Climbing) &&
        !DragInteractionPolicy.CanStart(PetState.Dragging) &&
        !DragInteractionPolicy.CanStart(PetState.Rolling) &&
        !DragInteractionPolicy.CanStart(PetState.Waking),
        "Short atomic animations and an active drag should retain their drag guard.");
}

static void WindowEdgesExposeEveryTerrainAction()
{
    Assert(TerrainBehaviorPlanner.ChooseAtEdge(false, 0.1) == TerrainEdgeAction.Hop,
        "Window edges should allow a hop.");
    Assert(TerrainBehaviorPlanner.ChooseAtEdge(false, 0.25) == TerrainEdgeAction.SlideDown,
        "Window edges should allow sliding down the side.");
    Assert(TerrainBehaviorPlanner.ChooseAtEdge(false, 0.4) == TerrainEdgeAction.JumpDown,
        "Window edges should allow a direct jump down.");
    Assert(TerrainBehaviorPlanner.ChooseAtEdge(false, 0.8) == TerrainEdgeAction.TurnAround,
        "Window edges should still allow an ordinary turn.");
    Assert(TerrainBehaviorPlanner.ChooseAtEdge(true, 0.3) == TerrainEdgeAction.TurnAround,
        "Desktop floor edges must not start a wall slide into empty space.");
}

static void MaximizedSupportLaunchesAwayFromCenter()
{
    Assert(WindowExpansionPhysics.Evaluate(false, true, 300, 500, 1) is null,
        "A still-live support should continue normal platform tracking.");
    Assert(WindowExpansionPhysics.Evaluate(true, false, 300, 500, 1) is null,
        "An ordinary disappearing window should create a fall, not a maximize bounce.");

    var left = WindowExpansionPhysics.Evaluate(true, true, 300, 500, 1.5);
    var right = WindowExpansionPhysics.Evaluate(true, true, 700, 500, 1.5);
    Assert(left is { VelocityX: < 0, VelocityY: < -1000 },
        "A pet on the left half should be kicked left and upward at scaled speed.");
    Assert(right is { VelocityX: > 0, VelocityY: < -1000 },
        "A pet on the right half should be kicked right and upward at scaled speed.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
