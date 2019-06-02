using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using AirtableApiClient;
using System.Linq;
using System.Collections.Generic;

namespace infin8x
{
    public static class RotationParser
    {
        [FunctionName("RotationParser")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            string rotation = await new StreamReader(req.Body).ReadToEndAsync();

            using (AirtableBase airtableBase = new AirtableBase(
                Environment.GetEnvironmentVariable("airtableApiKey"),
                Environment.GetEnvironmentVariable("airtableBaseId")))
            {
                log.LogInformation("Airtable connection initialized.");

                var rotationNumber = int.Parse(new Regex(@"(^\d+)", RegexOptions.Multiline).Match(rotation).Groups[1].Value);
                var rotationStartDateString = new Regex(@" (\d{2}[A-Z]{3})").Match(rotation).Groups[1].Value;
                var rotationStartDate = monthConverter[rotationStartDateString.Substring(2, 3)] + "/" +
                                                       int.Parse(rotationStartDateString.Substring(0, 2)) + "/" +
                                                       DateTime.Now.Year;
                var rotationNumberOfAttendants = new Regex(@"([0-9]+) F\/A").Match(rotation).Groups[1].Value;
                log.LogInformation($"Parsed rotation #{rotationNumber}-{rotationStartDate}.");

                // Update rotation
                log.LogInformation("Updating rotation.");
                var rotationRecordId = req.Headers["Airtable-Record-Id"][0];
                var rotationLookup = airtableBase.RetrieveRecord("Rotations", rotationRecordId).GetAwaiter().GetResult();
                if (!rotationLookup.Success)
                {
                    log.LogError(rotationLookup.AirtableApiError.ErrorMessage);
                    return new StatusCodeResult(500);
                }
                log.LogInformation("Looked up rotation successfully.");
                var rotationFields = new Fields();
                rotationFields.AddField("Rotation #", rotationNumber);
                rotationFields.AddField("Date", rotationStartDate);
                var rotationUpdate = airtableBase.UpdateRecord("People", rotationFields, rotationRecordId).GetAwaiter().GetResult();
                if (!rotationUpdate.Success)
                {
                    log.LogError(rotationUpdate.AirtableApiError.ErrorMessage);
                    return new StatusCodeResult(500);
                }
                log.LogInformation("Updated rotation successfully.");

                // Add flight attendants
                for (int i = 0; i < int.Parse(rotationNumberOfAttendants); i++)
                {
                    log.LogInformation($"Starting to process flight attendant {i}.");
                    var flightAttendantRecord = new Regex($@"^{Convert.ToChar(65 + i)} 0([0-9]*) ([A-Za-z]*)([A-Za-z ]*)", RegexOptions.Multiline).Match(rotation);
                    var faEmployeeId = int.Parse(flightAttendantRecord.Groups[1].Value);

                    var faLookup = airtableBase.ListRecords("People", null, null, $"{{Employee ID}} = {faEmployeeId}").GetAwaiter().GetResult();

                    if (!faLookup.Success)
                    {
                        log.LogError(faLookup.AirtableApiError.ErrorMessage);
                        return new StatusCodeResult(500);
                    }
                    log.LogInformation($"Looked up flight attendant {faLookup} successfully.");

                    if (!faLookup.Records.Any())
                    {
                        log.LogInformation("Adding flight attendant.");
                        var fields = new Fields();
                        fields.AddField("Employee ID", faEmployeeId);
                        fields.AddField("First name", flightAttendantRecord.Groups[2].Value);
                        fields.AddField("Last name", flightAttendantRecord.Groups[3].Value);
                        var result = airtableBase.CreateRecord("People", fields).GetAwaiter().GetResult();
                        if (!result.Success)
                        {
                            log.LogError(result.AirtableApiError.ErrorMessage);
                            return new StatusCodeResult(500);
                        }
                        log.LogInformation("Added flight attendant successfully.");
                    }
                    else
                    {
                        log.LogInformation("Flight attendant already registered.");
                    }
                }
            }
            return (ActionResult)new OkResult();
        }

        private static readonly Dictionary<string, int> monthConverter = new Dictionary<string, int> {
            {"JAN", 01},
            {"FEB", 02},
            {"MAR", 03},
            {"APR", 04},
            {"MAY", 05},
            {"JUN", 06},
            {"JUL", 07},
            {"AUG", 08},
            {"SEP", 09},
            {"OCT", 10},
            {"NOV", 11},
            {"DEC", 12},
            };
    }
}
