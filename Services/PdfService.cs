using ECourtTracker.API.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ECourtTracker.API.Services
{
    public class PdfService : IPdfService
    {
        public byte[] GenerateCaseReport(ECourtCaseResultDto caseDetails)
        {
            // Ensure all lists are non-null
            caseDetails.Petitioners ??= new();
            caseDetails.Respondents ??= new();
            caseDetails.PetitionerAdvocates ??= new();
            caseDetails.RespondentAdvocates ??= new();
            caseDetails.HearingHistory ??= new();
            caseDetails.Processes ??= new();
            caseDetails.Acts ??= new();
            caseDetails.Orders ??= new();

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1.5f, Unit.Centimetre);
                    page.PageColor(Colors.White);
                    
                    // Use a very safe font family
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                    page.Header().Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("E-COURT TRACKER").FontSize(20).SemiBold().FontColor(Colors.Blue.Medium);
                            col.Item().Text("CASE INFORMATION REPORT").FontSize(12).FontColor(Colors.Grey.Medium);
                        });

                        row.RelativeItem().AlignRight().Column(col =>
                        {
                            col.Item().Text($"CNR: {caseDetails.CnrNumber ?? "N/A"}").FontSize(10).SemiBold();
                            col.Item().Text($"Generated: {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                    });

                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        // 1. Case Summary
                        col.Item().Background(Colors.Grey.Lighten4).Padding(5).Text("CASE SUMMARY").SemiBold();
                        col.Item().PaddingBottom(10).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(1);
                                columns.RelativeColumn(2);
                            });

                            AddRow(table, "Case Title", caseDetails.CaseTitle);
                            AddRow(table, "Case Type", caseDetails.CaseType);
                            AddRow(table, "Case Number", caseDetails.CaseNumber);
                            AddRow(table, "Filing Number", caseDetails.FilingNumber);
                            AddRow(table, "Filing Date", caseDetails.FilingDate);
                            AddRow(table, "Registration Number", caseDetails.RegistrationNumber);
                            AddRow(table, "Registration Date", caseDetails.RegistrationDate);
                            AddRow(table, "Case Status", caseDetails.CaseStatus);
                            AddRow(table, "Next Hearing Date", caseDetails.NextHearingDate);
                            AddRow(table, "Judge", caseDetails.JudgeName);
                            AddRow(table, "Court Number/Name", caseDetails.CourtEstablishment ?? caseDetails.CourtNumber);
                        });

                        // 2. Parties
                        col.Item().Background(Colors.Grey.Lighten4).Padding(5).Text("PARTIES").SemiBold();
                        col.Item().PaddingBottom(10).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Petitioner(s)").FontSize(9).SemiBold();
                                foreach (var p in caseDetails.Petitioners.DefaultIfEmpty(caseDetails.Petitioner ?? "—"))
                                    c.Item().Text(p).FontSize(9);
                                
                                c.Item().PaddingTop(5).Text("Advocate(s)").FontSize(8).SemiBold();
                                foreach (var a in caseDetails.PetitionerAdvocates.DefaultIfEmpty(caseDetails.AdvocateDetails ?? "—"))
                                    c.Item().Text(a).FontSize(8);
                            });

                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Respondent(s)").FontSize(9).SemiBold();
                                foreach (var r in caseDetails.Respondents.DefaultIfEmpty(caseDetails.Respondent ?? "—"))
                                    c.Item().Text(r).FontSize(9);

                                c.Item().PaddingTop(5).Text("Advocate(s)").FontSize(8).SemiBold();
                                foreach (var a in caseDetails.RespondentAdvocates.DefaultIfEmpty("—"))
                                    c.Item().Text(a).FontSize(8);
                            });
                        });

                        // 3. Acts
                        if (caseDetails.Acts.Any())
                        {
                            col.Item().Background(Colors.Grey.Lighten4).Padding(5).Text("ACTS").SemiBold();
                            col.Item().PaddingBottom(10).Column(c =>
                            {
                                foreach (var act in caseDetails.Acts)
                                    c.Item().Text(act).FontSize(9);
                            });
                        }

                        // 4. History
                        if (caseDetails.HearingHistory.Any())
                        {
                            col.Item().Background(Colors.Grey.Lighten4).Padding(5).Text("HEARING HISTORY").SemiBold();
                            col.Item().PaddingBottom(10).Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(70);
                                    columns.RelativeColumn();
                                    columns.ConstantColumn(80);
                                    columns.ConstantColumn(80);
                                });

                                table.Header(h =>
                                {
                                    h.Cell().Text("Date").SemiBold();
                                    h.Cell().Text("Purpose").SemiBold();
                                    h.Cell().Text("Business On").SemiBold();
                                    h.Cell().Text("Next Date").SemiBold();
                                });

                                foreach (var h in caseDetails.HearingHistory)
                                {
                                    table.Cell().Text(h.Date).FontSize(8);
                                    table.Cell().Text(h.Purpose).FontSize(8);
                                    table.Cell().Text(h.BusinessOnDate).FontSize(8);
                                    table.Cell().Text(h.NextHearingDate).FontSize(8);
                                }
                            });
                        }

                        // 5. Processes
                        if (caseDetails.Processes.Any())
                        {
                            col.Item().Background(Colors.Grey.Lighten4).Padding(5).Text("PROCESS ISSUANCE").SemiBold();
                            col.Item().PaddingBottom(10).Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(100);
                                    columns.RelativeColumn();
                                    columns.ConstantColumn(80);
                                });

                                table.Header(h =>
                                {
                                    h.Cell().Text("Process ID").SemiBold();
                                    h.Cell().Text("Type").SemiBold();
                                    h.Cell().Text("Issued Date").SemiBold();
                                });

                                foreach (var p in caseDetails.Processes)
                                {
                                    table.Cell().Text(p.ProcessId).FontSize(8);
                                    table.Cell().Text(p.Title).FontSize(8);
                                    table.Cell().Text(p.Date).FontSize(8);
                                }
                            });
                        }
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Page ");
                        x.CurrentPageNumber();
                        x.Span(" of ");
                        x.TotalPages();
                    });
                });
            }).GeneratePdf();
        }

        private void AddRow(TableDescriptor table, string label, string? value)
        {
            table.Cell().Text(label).FontSize(9).SemiBold();
            table.Cell().Text(value ?? "—").FontSize(9);
        }
    }
}
