# Care homes — proposed cleanup

Status: COMPLETED on 2026-10-02 after owner confirmation. Exact paths are relative to D:/Sanad_API.

Owner clarified that cleanup includes irrelevant original legacy workflows/docs/handoffs, not only Batch A. The reconciled manifest below records the confirmed deletion; it supersedes the historical 21-file list where current evidence, dirty files, or user changes required retention.

## Reconciled confirmation manifest

The confirmed deletion set was **118 exact files**: all 102 withdrawn generated task drafts listed in Batch A, plus the following 16 unchanged legacy sources. These exact files were deleted individually; no directory was deleted recursively.

### Additional legacy sources deleted (16)

1. `Sanad_Master_Context.md`
2. `Sanad_Operations.md`
3. `subscription-vat-tax/worker-bruno-order-correction.txt`
4. `subscription-vat-tax/worker-tax-concurrency-test-retry.txt`
5. `subscription-vat-tax/worker-tax-concurrency-test.txt`
6. `subscription-vat-tax/worker-vat-tax-correction-01.txt`
7. `subscription-vat-tax/worker-vat-tax-implementation.txt`
8. `subscription-vat-tax/worker-vat-tax-test-correction-01.txt`
9. `subscription-vat-tax/worker-vat-tax-tests.txt`
10. `docs/goal-progress.md`
11. `docs/next-goal-tasks.md`
12. `docs/operations/current-phase-todo.md`
13. `docs/operations/application-roadmap.md`
14. `tools/Publish-SanadApi.ps1`
15. `tools/Verify-ApiContractMapping.ps1`
16. `tools/Verify-SanadDeployment.ps1`

### Replacement mapping

| Proposed deletion | Retained replacement or disposition |
|---|---|
| `Sanad_Master_Context.md` | Sanitized scope and rules in `Project_Main_Goal.md`, `docs/operations/care-homes/Care_Homes_Decisions.md`, and owning reference docs; private environment details are not retained. |
| `Sanad_Operations.md` | Current state and next action in `Mastermind_Handoff.md`; feature evidence remains in retained modular docs. |
| `subscription-vat-tax/*.txt` | Subscription tax constraints and evidence preserved in `docs/slices/subscriptions/Subscription_Tax_Reference.md`; no worker brief remains active. |
| `docs/goal-progress.md` | Historical product-progress log; current Care homes status is owned by `Mastermind_Handoff.md` and `Care_Homes_Tasks.md`. |
| `docs/next-goal-tasks.md` | Future work intentionally remains unplanned; priority direction is in `Project_Main_Goal.md`. |
| `docs/operations/current-phase-todo.md` | Superseded release/phase workflow; current governance and Care homes execution ownership are in `docs/governance/AGENTS.md`, `docs/operations/codex-workflow.md`, and the Care homes handoff. |
| `docs/operations/application-roadmap.md` | Project scope and phase direction are in `Project_Main_Goal.md`; no future task backlog is reinstated. |
| `tools/Publish-SanadApi.ps1` | `docs/tools/Publish-SanadApi.ps1`; retained deployment guide already calls the new path. |
| `tools/Verify-ApiContractMapping.ps1` | `docs/tools/Verify-ApiContractMapping.ps1`; retained tests and coverage docs already call the new path. |
| `tools/Verify-SanadDeployment.ps1` | `docs/tools/Verify-SanadDeployment.ps1`; retained deployment guide already calls the new path. |

### Explicit exclusions from the confirmed deletion

Retain `docs/operations/family-ui-audit.md`, `docs/operations/elderly-ui-audit.md`, `docs/operations/endpoint-coverage-matrix.md`, and `docs/operations/endpoint-coverage-tracker.md` as current source/evidence references. Retain the modified `tools/Start-LocalBrunoFixtureApi.ps1`, all source code, UI, tests, Bruno/Postman collections, fixtures, runtime configuration, `docs/tools`, governance, Care homes files, and unrelated changes. The 102-file Batch A list below remains the exact generated-file portion of the 118-file manifest.

## Batch A: superseded generated product drafts (102 files)

These generated task files belong to the withdrawn whole-project plan. The retained Care homes decisions/tasks replace the four provisional Care homes drafts. Other product work stays unplanned; original legacy sources retain its evidence. Keep docs/slices/documentation/Documentation_Migration_Tasks.md as historical migration evidence.

Before removal, each exact file was rechecked as an unchanged generated draft or confirmed legacy source. Generated untracked drafts and private handoffs are not recoverable through Git unless separately backed up.

1. `docs/slices/foundations/Account_Settings_Tasks.md`
2. `docs/slices/foundations/Avatar_Tasks.md`
3. `docs/slices/foundations/Booking_Lifecycle_Tasks.md`
4. `docs/slices/foundations/Booking_Payments_Refunds_Tasks.md`
5. `docs/slices/foundations/Care_Assessment_Tasks.md`
6. `docs/slices/foundations/Caregiver_Availability_Tasks.md`
7. `docs/slices/foundations/Caregiver_Certificates_Tasks.md`
8. `docs/slices/foundations/Caregiver_Onboarding_Tasks.md`
9. `docs/slices/foundations/Caregiver_Pricing_Tasks.md`
10. `docs/slices/foundations/Caregiver_Profile_Tasks.md`
11. `docs/slices/foundations/Caregiver_Review_Tasks.md`
12. `docs/slices/foundations/Dependents_Tasks.md`
13. `docs/slices/foundations/Elderly_OTP_Tasks.md`
14. `docs/slices/foundations/Family_Invitations_Tasks.md`
15. `docs/slices/foundations/Family_Medications_Tasks.md`
16. `docs/slices/foundations/Family_Membership_Tasks.md`
17. `docs/slices/foundations/Help_Support_Tasks.md`
18. `docs/slices/foundations/Identity_Documents_Tasks.md`
19. `docs/slices/foundations/Legal_Content_Tasks.md`
20. `docs/slices/foundations/Login_Tasks.md`
21. `docs/slices/foundations/Medical_Profile_Tasks.md`
22. `docs/slices/foundations/Medical_Reports_Tasks.md`
23. `docs/slices/foundations/Notes_Activities_Tasks.md`
24. `docs/slices/foundations/Password_Recovery_Tasks.md`
25. `docs/slices/foundations/Public_Lookups_Tasks.md`
26. `docs/slices/foundations/Register_Tasks.md`
27. `docs/slices/foundations/Sessions_Tasks.md`
28. `docs/slices/foundations/Splash_Content_Tasks.md`
29. `docs/slices/foundations/Visit_Reports_Tasks.md`
30. `docs/slices/phase-00/Admin_UI_Audit_Tasks.md`
31. `docs/slices/phase-00/Caregiver_UI_Audit_Tasks.md`
32. `docs/slices/phase-00/Elderly_UI_Audit_Tasks.md`
33. `docs/slices/phase-00/Family_UI_Audit_Tasks.md`
34. `docs/slices/phase-00/Invitation_Outbox_Tasks.md`
35. `docs/slices/phase-00/Onboarding_Integration_Tasks.md`
36. `docs/slices/phase-01/Chat_Attachments_Tasks.md`
37. `docs/slices/phase-01/Chat_Notifications_Tasks.md`
38. `docs/slices/phase-01/Chat_Read_State_Tasks.md`
39. `docs/slices/phase-01/Chat_Realtime_Tasks.md`
40. `docs/slices/phase-01/Conversations_Tasks.md`
41. `docs/slices/phase-01/Messages_Tasks.md`
42. `docs/slices/phase-02/Email_Delivery_Tasks.md`
43. `docs/slices/phase-02/Notification_Admin_Operations_Tasks.md`
44. `docs/slices/phase-02/Notification_Event_Contracts_Tasks.md`
45. `docs/slices/phase-02/Notification_Inbox_Tasks.md`
46. `docs/slices/phase-02/Notification_Outbox_Tasks.md`
47. `docs/slices/phase-02/Notification_Retention_Tasks.md`
48. `docs/slices/phase-02/Notification_Scheduling_Tasks.md`
49. `docs/slices/phase-02/Push_Delivery_Tasks.md`
50. `docs/slices/phase-03/Admin_Sensitive_Data_Tasks.md`
51. `docs/slices/phase-03/Daily_CheckIn_Tasks.md`
52. `docs/slices/phase-03/Elderly_Dashboard_Tasks.md`
53. `docs/slices/phase-03/Elderly_Medications_Tasks.md`
54. `docs/slices/phase-03/Elderly_Profile_Tasks.md`
55. `docs/slices/phase-03/Elderly_Welcome_Tasks.md`
56. `docs/slices/phase-03/Emergency_Contact_Tasks.md`
57. `docs/slices/phase-03/Help_Requests_Tasks.md`
58. `docs/slices/phase-03/Medication_Lateness_Tasks.md`
59. `docs/slices/phase-03/SOS_Preferences_Tasks.md`
60. `docs/slices/phase-03/SOS_Tasks.md`
61. `docs/slices/phase-03/Sentence_Builder_Tasks.md`
62. `docs/slices/phase-04/Booking_Execution_Integration_Tasks.md`
63. `docs/slices/phase-04/Booking_Medical_Access_Tasks.md`
64. `docs/slices/phase-04/Caregiver_Dashboard_Tasks.md`
65. `docs/slices/phase-04/Caregiver_Discovery_Integration_Tasks.md`
66. `docs/slices/phase-04/Family_Dashboard_Tasks.md`
67. `docs/slices/phase-05/Care_Home_Catalog_Tasks.md`
68. `docs/slices/phase-05/Care_Home_Inventory_Tasks.md`
69. `docs/slices/phase-05/Care_Home_Long_Stay_Tasks.md`
70. `docs/slices/phase-05/Care_Home_Visits_Tasks.md`
71. `docs/slices/phase-06/Community_Interactions_Tasks.md`
72. `docs/slices/phase-06/Community_Moderation_Tasks.md`
73. `docs/slices/phase-06/Community_Posts_Tasks.md`
74. `docs/slices/phase-06/Library_App_Tasks.md`
75. `docs/slices/phase-06/Library_CMS_Tasks.md`
76. `docs/slices/phase-06/Saved_Content_Tasks.md`
77. `docs/slices/phase-06/Wellness_Clinical_Governance_Tasks.md`
78. `docs/slices/phase-06/Wellness_Tips_Tasks.md`
79. `docs/slices/phase-07/Medical_Access_Grants_Tasks.md`
80. `docs/slices/phase-07/Medical_Sharing_Enforcement_Tasks.md`
81. `docs/slices/phase-08/Caregiver_Earnings_Tasks.md`
82. `docs/slices/phase-08/Caregiver_Ratings_Tasks.md`
83. `docs/slices/phase-08/Family_Financial_History_Tasks.md`
84. `docs/slices/phase-08/Payouts_Tasks.md`
85. `docs/slices/phase-09/Service_Discovery_Integration_Tasks.md`
86. `docs/slices/phase-09/Unified_Search_Tasks.md`
87. `docs/slices/phase-10/API_Contract_Coverage_Tasks.md`
88. `docs/slices/phase-10/Cross_Role_Authorization_Tasks.md`
89. `docs/slices/phase-10/Privacy_Concurrency_Hardening_Tasks.md`
90. `docs/slices/phase-10/Release_Readiness_Tasks.md`
91. `docs/slices/subscriptions/Subscription_Allowances_Tasks.md`
92. `docs/slices/subscriptions/Subscription_Callbacks_Tasks.md`
93. `docs/slices/subscriptions/Subscription_Card_Enrollment_Tasks.md`
94. `docs/slices/subscriptions/Subscription_Coupons_Tasks.md`
95. `docs/slices/subscriptions/Subscription_Deferred_Enhancements_Tasks.md`
96. `docs/slices/subscriptions/Subscription_Invoices_Tasks.md`
97. `docs/slices/subscriptions/Subscription_Payments_Tasks.md`
98. `docs/slices/subscriptions/Subscription_Plan_Changes_Tasks.md`
99. `docs/slices/subscriptions/Subscription_Plans_Tasks.md`
100. `docs/slices/subscriptions/Subscription_Quotes_Tasks.md`
101. `docs/slices/subscriptions/Subscription_Renewal_Tasks.md`
102. `docs/slices/subscriptions/Subscription_Tax_Tasks.md`

## Historical Batch B comparison and retained exclusions

The 21-file historical list in [migration evidence](../slices/documentation/Documentation_Migration_Tasks.md) was reconciled before deletion. The 16 unchanged legacy sources in the confirmed manifest were removed; current audits, coverage evidence, and the modified fixture helper were retained. No private source contents were republished.

UI screenshots, tests, Bruno/Postman collections, protected fixtures, existing API/reference documentation, deployment/configuration files, docs/tools and the root AGENTS.md discovery pointer are excluded from cleanup.
