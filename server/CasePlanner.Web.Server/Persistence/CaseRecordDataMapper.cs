using System.Data.Common;
using CasePlanner.Web.Server.Models;

namespace CasePlanner.Web.Server.Persistence;

internal static class CaseRecordDataMapper
{
    public static CaseRecord Read(DbDataReader reader)
    {
        return new CaseRecord
        {
            Id = reader.GetInt64(0),
            CaseNumber = reader.GetString(1),
            CaseName = reader.GetString(2),
            JobNumber = String(reader, 3) ?? "",
            Tract = String(reader, 4) ?? "",
            County = String(reader, 5) ?? "",
            Status = String(reader, 6) ?? "",
            FilingDate = Date(reader, 7),
            DateOfTaking = Date(reader, 8),
            TrialDate = Date(reader, 9),
            NextAction = String(reader, 10),
            NextActionDue = Date(reader, 11),
            DepositAmount = Decimal(reader, 12),
            Owner = String(reader, 13),
            Landowner = String(reader, 14),
            ValuationNotes = String(reader, 15),
            SettlementNotes = String(reader, 16),
            PublicationServiceNotes = String(reader, 17),
            ServiceRequired = reader.IsDBNull(18) || Bool(reader, 18),
            ServicePerfected = !reader.IsDBNull(19) && Bool(reader, 19),
            ServicePerfectedDate = Date(reader, 20),
            ServiceDeadline120 = Date(reader, 21),
            ServiceDeadlineBasisDate = Date(reader, 22),
            ServiceMethod = String(reader, 23),
            ServiceNotes = String(reader, 24),
            ServiceStatus = String(reader, 25),
            CreatedAt = String(reader, 26),
            UpdatedAt = String(reader, 27),
            Stage = String(reader, 28) ?? "",
            Track = String(reader, 29) ?? "Contested",
            AssignedAttorney = String(reader, 30),
            OpposingCounsel = String(reader, 31),
            Appraiser = String(reader, 32),
            TaxesOwed = String(reader, 33),
            FundsWithdrawn = String(reader, 34),
            FundsWithdrawnDate = Date(reader, 35),
            DiscoveryCompleted = String(reader, 36),
            UpdatedAppraisal = String(reader, 37),
            ClosedDate = Date(reader, 38),
            ProjectName = String(reader, 39),
            TaxOwedAmount = Decimal(reader, 40),
            WholePropertyAcres = Decimal(reader, 41),
            AcquisitionAcres = Decimal(reader, 42),
            LandownerAppraiserName = String(reader, 43),
            AdditionalDepositAmount = Decimal(reader, 44),
            AdditionalDepositDate = Date(reader, 45),
            MatterType = string.IsNullOrWhiteSpace(String(reader, 46)) ? "FiledCase" : String(reader, 46)!,
            Priority = string.IsNullOrWhiteSpace(String(reader, 47)) ? "Normal" : String(reader, 47)!,
            CurrentHolder = String(reader, 48),
            PipelineStage = String(reader, 49),
            DateSentToCurrentHolder = Date(reader, 50),
            NextReviewDate = Date(reader, 51),
            LastMeaningfulActivityDate = String(reader, 52),
            MomentumStatus = String(reader, 53),
            WaitingReason = String(reader, 54),
            WaitingOn = String(reader, 55),
            WaitingStartedDate = Date(reader, 56),
            ExpectedResponse = String(reader, 57),
            WaitingFollowUpDate = Date(reader, 58),
            WaitingEscalationAction = String(reader, 59),
            TrialTrack = !reader.IsDBNull(60) && Bool(reader, 60),
            ShortPostureSummary = String(reader, 61),
            CurrentIssue = String(reader, 62),
            DeferredUntil = Date(reader, 63),
            DeferredReason = String(reader, 64),
            DeferredAt = String(reader, 65),
            DeferredBy = String(reader, 66),
            ChecklistTotal = reader.IsDBNull(67) ? 0 : Convert.ToInt32(reader.GetValue(67)),
            ChecklistDone = reader.IsDBNull(68) ? 0 : Convert.ToInt32(reader.GetValue(68)),
            CaseStatus = String(reader, 69) ?? "Pipeline",
            StatusMappingReview = !reader.IsDBNull(70) && Bool(reader, 70),
            DateOpened = reader.FieldCount > 71 ? Date(reader, 71) : null,
            RowVersion = reader.FieldCount > 72 && !reader.IsDBNull(72)
                ? Convert.ToBase64String((byte[])reader.GetValue(72))
                : null
        };
    }

    private static string? String(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal));

    private static decimal? Decimal(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToDecimal(reader.GetValue(ordinal));

    private static bool Bool(DbDataReader reader, int ordinal) => Convert.ToInt64(reader.GetValue(ordinal)) == 1;

    private static string? Date(DbDataReader reader, int ordinal)
    {
        var value = String(reader, ordinal);
        if (!DateOnly.TryParse(value, out var parsed) || parsed == new DateOnly(1900, 1, 1)) return null;
        return parsed.ToString("yyyy-MM-dd");
    }
}
