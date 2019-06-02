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
                var rotationNumber = new Regex(@"(^\d+)", RegexOptions.Multiline).Match(rotation).Groups[1].Value;
                var rotationStartDate = new Regex(@" (\d{2}[A-Z]{3})").Match(rotation).Groups[1].Value;
                var rotationStartDateParse = new Tuple<int, int>(
                    monthConverter[rotationStartDate.Substring(2, 3)],
                    int.Parse(rotationStartDate.Substring(0, 2))
                );

                var numberOfFAsInRotation = new Regex(@"([0-9]+) F\/A").Match(rotation).Groups[1].Value;

                for (int i = 0; i < int.Parse(numberOfFAsInRotation); i++)
                {
                    var flightAttendant = new Regex($@"^{Convert.ToChar(65 + i)} 0([0-9]*) ([A-Za-z]*)([A-Za-z ]*)", RegexOptions.Multiline).Match(rotation);
                    var faEmployeeId = int.Parse(flightAttendant.Groups[1].Value);

                    var faLookup = airtableBase.ListRecords("People", null, null, $"{{Employee ID}} = {faEmployeeId}").GetAwaiter().GetResult();
                    if (!faLookup.Records.Any())
                    {
                        var fields = new Fields();
                        fields.AddField("Employee ID", faEmployeeId);
                        fields.AddField("First name", flightAttendant.Groups[2].Value);
                        fields.AddField("Last name", flightAttendant.Groups[3].Value);
                        var add = airtableBase.CreateRecord("People", fields).GetAwaiter().GetResult();
                    }
                }

                var rotationRecordId = req.Headers["Airtable-Record-Id"][0];
                var rotationLookup = airtableBase.RetrieveRecord("Rotations", rotationRecordId).GetAwaiter().GetResult();
                var rotationFields = new Fields();
                rotationFields.AddField("Rotation #", int.Parse(rotationNumber));
                rotationFields.AddField("Date", 
                                        rotationStartDateParse.Item1 + "/" + rotationStartDateParse.Item2 + "/" + DateTime.Now.Year);
                airtableBase.UpdateRecord("People", rotationFields, rotationRecordId).GetAwaiter().GetResult();
            }
            return (ActionResult)new OkResult();
        }
    }
}
