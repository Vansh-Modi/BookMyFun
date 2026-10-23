using System.Runtime.Versioning;

namespace BookMyFun.Tests
{
    [SupportedOSPlatform("windows6.1")]
    public class DirectTestRunner
    {
        public static async Task<int> Main(string[] args)
        {
            Console.WriteLine("=== BOOKMYFUN MASTER VERIFICATION TEST SUITE ===");
            var imgTests = new ImageOptimizationTests();
            var otpTests = new OtpEmailDispatchTests();
            var loginTests = new LoginUiRefinementTests();
            var eventImgTests = new EventManagerImageUploadTests();
            var googleTests = new GoogleOAuthConfigTests();
            var propPermTests = new PropertyPermissionsTests();

            try
            {
                Console.WriteLine("\n--- TASK 1: STORAGE OPTIMIZATION SUITE ---");
                Console.Write("[RUNNING] Test 1: IsImageExtension_RecognizesStandardAndModernImageFormats... ");
                imgTests.IsImageExtension_RecognizesStandardAndModernImageFormats();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 2: IsModernImageFormat_IdentifiesWebPAndAvif... ");
                imgTests.IsModernImageFormat_IdentifiesWebPAndAvif();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 3: OptimizeImageAsync_DownscalesOversizedImageAndCompresses... ");
                await imgTests.OptimizeImageAsync_DownscalesOversizedImageAndCompresses();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 4: OptimizeImageAsync_ModernWebPFormat_PreservedDirectly... ");
                await imgTests.OptimizeImageAsync_ModernWebPFormat_PreservedDirectly();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 5: MediaStorageService_MagicBytes_AcceptsWebPAndAvif... ");
                await imgTests.MediaStorageService_MagicBytes_AcceptsWebPAndAvif();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 6: MediaStorageService_MagicBytes_RejectsExecutablePayloads... ");
                await imgTests.MediaStorageService_MagicBytes_RejectsExecutablePayloads();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 7: ClientSideOptimizerScript_ExistsAndDeclaresImageOptimizer... ");
                imgTests.ClientSideOptimizerScript_ExistsAndDeclaresImageOptimizer();
                Console.WriteLine("PASS");

                Console.WriteLine("\n--- TASK 14: OTP EMAIL DISPATCH & QUEUE SUITE ---");
                Console.Write("[RUNNING] Test 8: SendOtpEmailAsync_DispatchesSuccessfullyWithValidHtml... ");
                await otpTests.SendOtpEmailAsync_DispatchesSuccessfullyWithValidHtml();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 9: SendEmailAsync_RejectsInvalidRecipientAddresses... ");
                await otpTests.SendEmailAsync_RejectsInvalidRecipientAddresses();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 10: EmailQueueService_EnqueuesAndDequeuesMessagesReliably... ");
                await otpTests.EmailQueueService_EnqueuesAndDequeuesMessagesReliably();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 11: AccountController_SendOtp_ReturnsFailureWhenEmailFails... ");
                await otpTests.AccountController_SendOtp_ReturnsFailureWhenEmailFails();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 12: AccountController_SendOtp_SucceedsAndDispatchesOtpWhenServiceHealthy... ");
                await otpTests.AccountController_SendOtp_SucceedsAndDispatchesOtpWhenServiceHealthy();
                Console.WriteLine("PASS");

                Console.WriteLine("\n--- TASK 13: LOGIN UI REFINEMENT SUITE ---");
                Console.Write("[RUNNING] Test 13: LoginViewModel_RejectsNonEmailFormat... ");
                loginTests.LoginViewModel_RejectsNonEmailFormat();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 14: LoginViewModel_RequiresEmailAddress... ");
                loginTests.LoginViewModel_RequiresEmailAddress();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 15: LoginViewModel_MetadataHasNoUserIdRemnants... ");
                loginTests.LoginViewModel_MetadataHasNoUserIdRemnants();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 16: LoginView_TemplateIsStandardizedOnEmailOnly... ");
                loginTests.LoginView_TemplateIsStandardizedOnEmailOnly();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 17: AccountController_LoginFailedMessage_StandardizedOnEmailOnly... ");
                await loginTests.AccountController_LoginFailedMessage_StandardizedOnEmailOnly();
                Console.WriteLine("PASS");

                Console.WriteLine("\n--- TASK 2: EVENT MANAGER IMAGE UPLOAD SUITE ---");
                Console.Write("[RUNNING] Test 18: CreatePackage_WithDirectImageUpload_SavesFileViaMediaStorageAndSetsMediaUrl... ");
                await eventImgTests.CreatePackage_WithDirectImageUpload_SavesFileViaMediaStorageAndSetsMediaUrl();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 19: CreatePackage_WithExternalUrl_UsesMediaUrlDirectly... ");
                await eventImgTests.CreatePackage_WithExternalUrl_UsesMediaUrlDirectly();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 20: EditPackage_WithDirectImageUpload_ReplacesMediaUrl... ");
                await eventImgTests.EditPackage_WithDirectImageUpload_ReplacesMediaUrl();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 21: EditPackage_WithoutNewUpload_PreservesExistingMediaUrl... ");
                await eventImgTests.EditPackage_WithoutNewUpload_PreservesExistingMediaUrl();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 22: UpdateSetupStatus_WithProofFiles_UploadsAndMergesProofUrls... ");
                await eventImgTests.UpdateSetupStatus_WithProofFiles_UploadsAndMergesProofUrls();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 23: Views_CreateAndEditPackage_DeclareMultipartEnctypeAndImageFileInputs... ");
                eventImgTests.Views_CreateAndEditPackage_DeclareMultipartEnctypeAndImageFileInputs();
                Console.WriteLine("PASS");

                Console.WriteLine("\n--- TASK 15: GOOGLE OAUTH CONFIG SUITE ---");
                Console.Write("[RUNNING] Test 24: DirectGoogleLogin_ActiveProjectEmail_AuthenticatesAndLinksProvider... ");
                await googleTests.DirectGoogleLogin_ActiveProjectEmail_AuthenticatesAndLinksProvider();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 25: DirectGoogleLogin_InvalidModel_RedisplaysPromptWithActiveProjectEmail... ");
                await googleTests.DirectGoogleLogin_InvalidModel_RedisplaysPromptWithActiveProjectEmail();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 26: ExternalLogin_WhenMiddlewareUnconfigured_SuppliesActiveProjectEmailAndConfig... ");
                await googleTests.ExternalLogin_WhenMiddlewareUnconfigured_SuppliesActiveProjectEmailAndConfig();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 27: ExternalLoginPromptView_ContainsActiveProjectEmailAndCredentialBindings... ");
                googleTests.ExternalLoginPromptView_ContainsActiveProjectEmailAndCredentialBindings();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 28: AdminSettingsView_ContainsGoogleOAuthTabAndConfigurationForm... ");
                googleTests.AdminSettingsView_ContainsGoogleOAuthTabAndConfigurationForm();
                Console.WriteLine("PASS");

                Console.WriteLine("\n--- PROPERTY PERMISSIONS: ADMIN & EMPLOYEE FULL CONTROL SUITE ---");
                Console.Write("[RUNNING] Test 29: AdminController_DeactivateProperty_SetsStatusToDeactivatedAndAudits... ");
                await propPermTests.AdminController_DeactivateProperty_SetsStatusToDeactivatedAndAudits();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 30: EmployeeController_DeactivateProperty_SetsStatusToDeactivatedAndAudits... ");
                await propPermTests.EmployeeController_DeactivateProperty_SetsStatusToDeactivatedAndAudits();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 31: AdminController_DeleteProperty_PermanentlyDeletesPropertyAndAudits... ");
                await propPermTests.AdminController_DeleteProperty_PermanentlyDeletesPropertyAndAudits();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 32: EmployeeController_DeleteProperty_PermanentlyDeletesPropertyAndAudits... ");
                await propPermTests.EmployeeController_DeleteProperty_PermanentlyDeletesPropertyAndAudits();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 33: EmployeeController_ApproveProperty_UpdatesApprovalStatusAndAudits... ");
                await propPermTests.EmployeeController_ApproveProperty_UpdatesApprovalStatusAndAudits();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 34: Views_ManageProperties_ContainsDeactivateAndPermanentDeleteButtonsForAdminAndEmployee... ");
                propPermTests.Views_ManageProperties_ContainsDeactivateAndPermanentDeleteButtonsForAdminAndEmployee();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 35: OwnerController_DeactivateProperty_SetsStatusToDeactivatedAndAudits... ");
                await propPermTests.OwnerController_DeactivateProperty_SetsStatusToDeactivatedAndAudits();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 36: OwnerController_ActivateProperty_SetsStatusToActiveAndAudits... ");
                await propPermTests.OwnerController_ActivateProperty_SetsStatusToActiveAndAudits();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 37: OwnerController_DeactivateProperty_BelongingToAnotherOwner_ThrowsForbiddenException... ");
                await propPermTests.OwnerController_DeactivateProperty_BelongingToAnotherOwner_ThrowsForbiddenException();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 38: OwnerController_DeleteProperty_ThrowsForbiddenException... ");
                propPermTests.OwnerController_DeleteProperty_ThrowsForbiddenException();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 39: OwnerViews_ContainDeactivateAndPublishControls_AndExplicitlyOmitDeleteProperty... ");
                propPermTests.OwnerViews_ContainDeactivateAndPublishControls_AndExplicitlyOmitDeleteProperty();
                Console.WriteLine("PASS");

                Console.WriteLine("\n--- TASK 3: SERVICEABLE AREAS UI SUITE ---");
                var serviceAreaTests = new ServiceableAreasTests();

                Console.Write("[RUNNING] Test 40: UpdateServiceAreas_WithSelectedAreas_UpdatesRepoAndLogsAudit... ");
                await serviceAreaTests.UpdateServiceAreas_WithSelectedAreas_UpdatesRepoAndLogsAudit();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 41: UpdateServiceAreas_WithNullOrEmpty_ClearsServiceAreas... ");
                await serviceAreaTests.UpdateServiceAreas_WithNullOrEmpty_ClearsServiceAreas();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 42: ServiceAreas_Get_ReturnsViewModelWithDistinctLocationsAndSelectedAreas... ");
                await serviceAreaTests.ServiceAreas_Get_ReturnsViewModelWithDistinctLocationsAndSelectedAreas();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 43: ServiceAreasView_ContainsChipsTagsInputMapBoundariesAndSearch... ");
                serviceAreaTests.ServiceAreasView_ContainsChipsTagsInputMapBoundariesAndSearch();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 44: DashboardAndIndexViews_ContainRedesignedMultiSelectChipsWidget... ");
                serviceAreaTests.DashboardAndIndexViews_ContainRedesignedMultiSelectChipsWidget();
                Console.WriteLine("PASS");

                Console.WriteLine("\n--- TASK 10: OWNER LOCATION LINKS SUITE ---");
                var locationLinkTests = new OwnerLocationLinksTests();

                Console.Write("[RUNNING] Test 45: CreateProperty_WithMapUrl_PersistsMapUrl... ");
                await locationLinkTests.CreateProperty_WithMapUrl_PersistsMapUrl();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 46: EditProperty_UpdatingMapUrl_UpdatesAndPreservesMapUrl... ");
                await locationLinkTests.EditProperty_UpdatingMapUrl_UpdatesAndPreservesMapUrl();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 47: UpdateLocationUrl_ValidGoogleMapsUrl_UpdatesMapUrlAndLogsAudit... ");
                await locationLinkTests.UpdateLocationUrl_ValidGoogleMapsUrl_UpdatesMapUrlAndLogsAudit();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 48: UpdateLocationUrl_InvalidUrlScheme_RejectsAndSetsTempDataError... ");
                await locationLinkTests.UpdateLocationUrl_InvalidUrlScheme_RejectsAndSetsTempDataError();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 49: UpdateLocationUrl_BelongingToAnotherOwner_ThrowsForbiddenException... ");
                await locationLinkTests.UpdateLocationUrl_BelongingToAnotherOwner_ThrowsForbiddenException();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 50: OwnerViews_CreateEditIndexAndDetails_ContainMapUrlControlsAndDirectLinks... ");
                locationLinkTests.OwnerViews_CreateEditIndexAndDetails_ContainMapUrlControlsAndDirectLinks();
                Console.WriteLine("PASS");

                Console.WriteLine("\n--- TASK 9: OWNER CATEGORY ASSIGNMENT SUITE ---");
                var categoryTests = new OwnerCategoryAssignmentTests();

                Console.Write("[RUNNING] Test 51: CreateProperty_WithCustomCategory_CreatesTypeAndAssignsPropertyTypeID... ");
                await categoryTests.CreateProperty_WithCustomCategory_CreatesTypeAndAssignsPropertyTypeID();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 52: EditProperty_UpdatingCategory_UpdatesPropertyTypeID... ");
                await categoryTests.EditProperty_UpdatingCategory_UpdatesPropertyTypeID();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 53: AssignCategory_ExistingCategory_UpdatesPropertyAndLogsAudit... ");
                await categoryTests.AssignCategory_ExistingCategory_UpdatesPropertyAndLogsAudit();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 54: AssignCategory_CustomCategory_CreatesNewTypeAndAssigns... ");
                await categoryTests.AssignCategory_CustomCategory_CreatesNewTypeAndAssigns();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 55: AssignCategory_BelongingToAnotherOwner_ThrowsForbiddenException... ");
                await categoryTests.AssignCategory_BelongingToAnotherOwner_ThrowsForbiddenException();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 56: OwnerViews_CreateEditAndIndex_ContainCategoryControlsAndModals... ");
                categoryTests.OwnerViews_CreateEditAndIndex_ContainCategoryControlsAndModals();
                Console.WriteLine("PASS");

                Console.WriteLine("\n--- TASK 5: COMMISSION ARCHITECTURE & ADMINISTRATIVE OVERRIDES SUITE ---");
                var commissionTests = new CommissionArchitectureTests();

                Console.Write("[RUNNING] Test 57: CalculateEventCommission_AppliesDefaultGlobalStandingRate_WhenNoOverridesExist... ");
                await commissionTests.CalculateEventCommission_AppliesDefaultGlobalStandingRate_WhenNoOverridesExist();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 58: UpdateGlobalEventCommission_AdminAndEmployee_UpdatesGlobalRateAndAudits... ");
                await commissionTests.UpdateGlobalEventCommission_AdminAndEmployee_UpdatesGlobalRateAndAudits();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 59: UpdateEventManagerCommission_AdminAndEmployee_AppliesCustomRatePerEventManager... ");
                await commissionTests.UpdateEventManagerCommission_AdminAndEmployee_AppliesCustomRatePerEventManager();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 60: UpdateEventManagerCommission_ResetToNull_RevertsToGlobalStandingRate... ");
                await commissionTests.UpdateEventManagerCommission_ResetToNull_RevertsToGlobalStandingRate();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 61: UpdateEventCommission_And_UpdateBookingEventCommission_PerEventOverrides... ");
                await commissionTests.UpdateEventCommission_And_UpdateBookingEventCommission_PerEventOverrides();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 62: CommissionOverrideEndpoints_RestrictedStrictlyToAdminAndEmployeeRoles... ");
                commissionTests.CommissionOverrideEndpoints_RestrictedStrictlyToAdminAndEmployeeRoles();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 63: EventCommissionsViews_ContainGlobalRateManagerOverridesAndEventPackageTables... ");
                commissionTests.EventCommissionsViews_ContainGlobalRateManagerOverridesAndEventPackageTables();
                Console.WriteLine("PASS");

                Console.WriteLine("\n--- TASK 11: EMPLOYEE PROPERTY APPROVAL WORKFLOW SUITE ---");
                var approvalTests = new EmployeePropertyApprovalTests();

                Console.Write("[RUNNING] Test 64: EmployeeApproveProperty_SetsIsApprovedTrue_AndListingStatusActive_AndAudits... ");
                await approvalTests.EmployeeApproveProperty_SetsIsApprovedTrue_AndListingStatusActive_AndAudits();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 65: EmployeeRejectProperty_SetsIsApprovedFalse_AndListingStatusRejected_AndAudits... ");
                await approvalTests.EmployeeRejectProperty_SetsIsApprovedFalse_AndListingStatusRejected_AndAudits();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 66: EmployeeRevokeProperty_SetsIsApprovedFalse_AndListingStatusDraft_AndAudits... ");
                await approvalTests.EmployeeRevokeProperty_SetsIsApprovedFalse_AndListingStatusDraft_AndAudits();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 67: EmployeeApproveProperty_NonExistentProperty_ReturnsNotFound... ");
                await approvalTests.EmployeeApproveProperty_NonExistentProperty_ReturnsNotFound();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 68: EmployeeApproveProperty_RedirectsToReturnUrl_WhenValid... ");
                await approvalTests.EmployeeApproveProperty_RedirectsToReturnUrl_WhenValid();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 69: EmployeeViews_ContainApprovalActionButtons_AndPendingReviewBadges... ");
                approvalTests.EmployeeViews_ContainApprovalActionButtons_AndPendingReviewBadges();
                Console.WriteLine("PASS");

                Console.WriteLine("\n--- TASK 4: DECORATION OFFERINGS & UPSELL SHOWCASE SUITE ---");
                var upsellTests = new DecorationUpsellUXTests();

                Console.Write("[RUNNING] Test 70: TogglePackageStatus_TogglesActiveState_AndLogsAudit... ");
                await upsellTests.TogglePackageStatus_TogglesActiveState_AndLogsAudit();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 71: TogglePackageStatus_WithReturnUrl_RedirectsToReturnUrl... ");
                await upsellTests.TogglePackageStatus_WithReturnUrl_RedirectsToReturnUrl();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 72: TogglePackageStatus_NonExistentPackage_ReturnsNotFound... ");
                await upsellTests.TogglePackageStatus_NonExistentPackage_ReturnsNotFound();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 73: DeletePackage_WithReturnUrl_RedirectsToReturnUrl... ");
                await upsellTests.DeletePackage_WithReturnUrl_RedirectsToReturnUrl();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 74: DashboardViews_ReorganizedHierarchy_ElevatesOfferingsShowcaseAboveOrdersAndServiceAreas... ");
                upsellTests.DashboardViews_ReorganizedHierarchy_ElevatesOfferingsShowcaseAboveOrdersAndServiceAreas();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 75: DashboardViews_OfferingsShowcase_DeclaresConversionBadgesAndInteractiveToggles... ");
                upsellTests.DashboardViews_OfferingsShowcase_DeclaresConversionBadgesAndInteractiveToggles();
                Console.WriteLine("PASS");

                Console.WriteLine("\n--- TASK 6: RECEIPT ENGINE & DESIGN SUITE ---");
                var receiptTests = new ReceiptEngineTests();

                Console.Write("[RUNNING] Test 76: BookingReceiptViewModel_FinancialCalculations_CorrectlyDerivesStayGrossAndHostNetPayout... ");
                receiptTests.BookingReceiptViewModel_FinancialCalculations_CorrectlyDerivesStayGrossAndHostNetPayout();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 77: BookingController_Receipt_ReturnsViewModelWithGuestAndStayDetails... ");
                await receiptTests.BookingController_Receipt_ReturnsViewModelWithGuestAndStayDetails();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 78: BookingController_Receipt_IDOR_ClientRole_ForbiddenForOtherGuests... ");
                await receiptTests.BookingController_Receipt_IDOR_ClientRole_ForbiddenForOtherGuests();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 79: OwnerController_BookingReceipt_ReturnsViewModelWithHostSettlementDetails... ");
                await receiptTests.OwnerController_BookingReceipt_ReturnsViewModelWithHostSettlementDetails();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 80: OwnerController_BookingReceipt_IDOR_OwnerRole_ThrowsForbiddenExceptionForOtherHosts... ");
                await receiptTests.OwnerController_BookingReceipt_IDOR_OwnerRole_ThrowsForbiddenExceptionForOtherHosts();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 81: Views_ReceiptsAndDashboards_DeclarePrintRulesAndDirectActionLinks... ");
                receiptTests.Views_ReceiptsAndDashboards_DeclarePrintRulesAndDirectActionLinks();
                Console.WriteLine("PASS");

                Console.WriteLine("\n--- TASK 7: DEDICATED CONFIRMATION PAGE & ROADMAP SUITE ---");
                var confirmTests = new DedicatedConfirmationPageTests();

                Console.Write("[RUNNING] Test 82: BookingController_Confirmation_ReturnsDedicatedViewModelWithMetadataAndReceiptLinks... ");
                await confirmTests.BookingController_Confirmation_ReturnsDedicatedViewModelWithMetadataAndReceiptLinks();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 83: BookingController_Confirmation_IDOR_ClientRole_ForbiddenForOtherGuests... ");
                await confirmTests.BookingController_Confirmation_IDOR_ClientRole_ForbiddenForOtherGuests();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 84: BookingController_Confirmation_AdminAndEmployee_AllowedToInspectAnyBooking... ");
                await confirmTests.BookingController_Confirmation_AdminAndEmployee_AllowedToInspectAnyBooking();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 85: ConfirmationView_ContainsNextStepsRoadmapAndAllFourPhases... ");
                confirmTests.ConfirmationView_ContainsNextStepsRoadmapAndAllFourPhases();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 86: ConfirmationView_ContainsDedicatedReceiptDownloadHubAndActionButtons... ");
                confirmTests.ConfirmationView_ContainsDedicatedReceiptDownloadHubAndActionButtons();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 87: SiteCss_ContainsDedicatedConfirmationPageAndRoadmapStyling... ");
                confirmTests.SiteCss_ContainsDedicatedConfirmationPageAndRoadmapStyling();
                Console.WriteLine("PASS");

                Console.WriteLine("\n--- TASK 12: RECEIPT TRANSACTIONAL DISPATCH SUITE ---");
                var dispatchTests = new ReceiptDispatchIntegrationTests();

                Console.Write("[RUNNING] Test 88: CommunicationService_SendBookingReceiptEmailAsync_GeneratesStructuredHtmlWithReceiptAndGatePass... ");
                await dispatchTests.CommunicationService_SendBookingReceiptEmailAsync_GeneratesStructuredHtmlWithReceiptAndGatePass();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 89: CommunicationService_SendBookingReceiptWhatsAppAsync_DispatchesCompleteReceiptDetailsAndLink... ");
                await dispatchTests.CommunicationService_SendBookingReceiptWhatsAppAsync_DispatchesCompleteReceiptDetailsAndLink();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 90: CommunicationService_SendOwnerSettlementReceiptEmailAsync_GeneratesHostStatementWithNetPayoutAndCommission... ");
                await dispatchTests.CommunicationService_SendOwnerSettlementReceiptEmailAsync_GeneratesHostStatementWithNetPayoutAndCommission();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 91: CommunicationService_SendOwnerBookingReceiptWhatsAppAsync_DispatchesSettlementSummaryAndLink... ");
                await dispatchTests.CommunicationService_SendOwnerBookingReceiptWhatsAppAsync_DispatchesSettlementSummaryAndLink();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 92: BookingController_ResendReceipt_EnforcesIDOR_AndDispatchesToClient... ");
                await dispatchTests.BookingController_ResendReceipt_EnforcesIDOR_AndDispatchesToClient();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 93: Views_ConfirmationAndReceipts_DeclareTransactionalDeliveryControls... ");
                dispatchTests.Views_ConfirmationAndReceipts_DeclareTransactionalDeliveryControls();
                Console.WriteLine("PASS");

                Console.WriteLine("\n--- TASK 16: THEME GLITCH RESOLUTION SUITE ---");
                var themeTests = new ThemeGlitchResolutionTests();

                Console.Write("[RUNNING] Test 94: LayoutAndSiteJs_ThemeAttributeSynchronization_SetsBothDataThemeAndDataBsTheme... ");
                themeTests.Test94_LayoutAndSiteJs_ThemeAttributeSynchronization_SetsBothDataThemeAndDataBsTheme();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 95: SiteCss_TokensDeclaredInLightAndDarkMode_UniformTokenInheritance... ");
                themeTests.Test95_SiteCss_TokensDeclaredInLightAndDarkMode_UniformTokenInheritance();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 96: SiteCss_BootstrapNativeOverridesAndUtilityEnhancements... ");
                themeTests.Test96_SiteCss_BootstrapNativeOverridesAndUtilityEnhancements();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 97: SiteCss_InputAutofillAndFormControls_DarkContrastIntegrity... ");
                themeTests.Test97_SiteCss_InputAutofillAndFormControls_DarkContrastIntegrity();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 98: PropertiesIndexView_NoHardcodedRootTokens_AndDeclaresLuxeDarkOverrides... ");
                themeTests.Test98_PropertiesIndexView_NoHardcodedRootTokens_AndDeclaresLuxeDarkOverrides();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 99: PaymentHeroCard_DeclaresThemeAwareGradient_AndViewsUseClass... ");
                themeTests.Test99_PaymentHeroCard_DeclaresThemeAwareGradient_AndViewsUseClass();
                Console.WriteLine("PASS");

                Console.WriteLine("\n--- TASK 17: RESPONSIVE LAYOUT & SIZING SUITE ---");
                var responsiveTests = new ResponsiveLayoutTests();

                Console.Write("[RUNNING] Test 100: SiteCss_DocumentAndViewportContainment_DeclaresOverflowClipAndImageFluidMaxBounds... ");
                responsiveTests.Test100_SiteCss_DocumentAndViewportContainment_DeclaresOverflowClipAndImageFluidMaxBounds();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 101: SiteCss_MobileFormInputs_PreventSafariAutoZoom_DeclaresMinimum16pxFontSize... ");
                responsiveTests.Test101_SiteCss_MobileFormInputs_PreventSafariAutoZoom_DeclaresMinimum16pxFontSize();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 102: SiteCss_TouchTargetStandards_AndTableContainment_DeclaresComfortableTapAreas... ");
                responsiveTests.Test102_SiteCss_TouchTargetStandards_AndTableContainment_DeclaresComfortableTapAreas();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 103: LuxeCatalog_ResponsiveLayoutAndCardActionsStacking_PreservesFluidGridAcrossBreakpoints... ");
                responsiveTests.Test103_LuxeCatalog_ResponsiveLayoutAndCardActionsStacking_PreservesFluidGridAcrossBreakpoints();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 104: DashboardAndHero_StatGridResponsiveness_CollapsesGracefullyFromDesktopToMobile... ");
                responsiveTests.Test104_DashboardAndHero_StatGridResponsiveness_CollapsesGracefullyFromDesktopToMobile();
                Console.WriteLine("PASS");

                Console.Write("[RUNNING] Test 105: ReceiptAndConfirmation_ResponsiveLayout_ScalesPaddingAndActionsForSmallViewports... ");
                responsiveTests.Test105_ReceiptAndConfirmation_ResponsiveLayout_ScalesPaddingAndActionsForSmallViewports();
                Console.WriteLine("PASS");

                Console.WriteLine("\n=== VERDICT: PASS -- ALL 105 TESTS PASSED WITH 100% SUCCESS ===");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n=== VERDICT: FAIL -- {ex.Message} ===");
                Console.WriteLine(ex.StackTrace);
                return 1;
            }
        }
    }
}
