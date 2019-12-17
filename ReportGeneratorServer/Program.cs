using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Palmmedia.ReportGenerator.Core;
using Palmmedia.ReportGenerator.Core.Parser;
using Palmmedia.ReportGenerator.Core.Parser.Analysis;
using Palmmedia.ReportGenerator.Core.Parser.Filtering;
using Red;
using Red.Validation;
using TokenBucket;

namespace ReportGeneratorServer
{
    class Program
    {
        const long MaxCoverageFileSize = 5242880;

        static async Task Main(string[] args)
        {
            var server = new RedHttpServer(5234);

            var tokenBucket = TokenBuckets.Construct()
                .WithCapacity(5)
                .WithFixedIntervalRefillStrategy(3, TimeSpan.FromSeconds(5))
                .Build();

            var fileValidator = Validation.ValidatorBuilder.New()
                .RequiresFile("coverageFile", file => file.Length < MaxCoverageFileSize)
                .BuildRedFormMiddleware();

            server.RespondWithExceptionDetails = true;

            server.Get("/", (req, res) => res.SendFile("index.html"));
            server.Post("/generate", fileValidator, async (req, res) =>
            {
                var form = await req.GetFormDataAsync();
                var file = form.Files["coverageFile"];

                var actualExtension = Path.GetExtension(file.FileName);
                
                var inputfile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + actualExtension);

                tokenBucket.Consume(1);

                try
                {
                    using (var tempFile = File.Create(inputfile))
                    {
                        await file.CopyToAsync(tempFile);
                    }

                    var summary = await GenerateReport(inputfile);


                    if (summary != default)
                    {
                        return await res.SendJson(new
                        {
                            summary.CoveredLines,
                            summary.CoverableLines,
                            LineCoverage = (summary.CoveredLines / (float) summary.CoverableLines) * 100,
                            Metrics = summary.SumableMetrics.Select(metric => new
                            {
                                metric.Name,
                                metric.Value
                            }).ToList(),
                            Assemblies = summary.Assemblies.Select(a => new
                            {
                                a.Name,
                                Classes = a.Classes.Select(c => new
                                {
                                    c.Name,
                                    LineCoverage = (c.CoveredLines / (float) c.CoverableLines) * 100,
                                    c.CoveredLines,
                                    c.CoverableLines,
                                }).ToList(),
                                LineCoverage = (a.CoveredLines / (float) a.CoverableLines) * 100,
                                a.CoveredLines,
                                a.CoverableLines
                            }).ToList()
                        });
                    }
                    else
                        return await res.SendStatus(HttpStatusCode.BadRequest);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return await res.SendStatus(HttpStatusCode.BadRequest);
                }
                finally
                {
                    File.Delete(inputfile);
                }
            });


            await server.RunAsync();
        }

        private static async Task<SummaryResult?> GenerateReport(string inputfile)
        {
            var reportArgs = new Dictionary<string, string>
            {
                { "VERBOSITY", "off" },
                { "REPORTS", inputfile }
            };
            var reportConfigurationBuilder = new ReportConfigurationBuilder();
            ReportConfiguration configuration = reportConfigurationBuilder.Create(reportArgs);
            var parserResult = new CoverageReportParser(1, 1, configuration.SourceDirectories,
                    new DefaultFilter(configuration.AssemblyFilters),
                    new DefaultFilter(configuration.ClassFilters),
                    new DefaultFilter(configuration.FileFilters))
                .ParseFiles(configuration.ReportFiles);

            var summaryResult = new SummaryResult(parserResult);
            return summaryResult;
        }
    }
}