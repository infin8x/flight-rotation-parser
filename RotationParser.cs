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
        // private const Dictionary<string,int> monthConverter = new Dictionary<string, int> {{"JAN", 01}};

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
                var rotationStartDateParse = new Tuple<int,int>(
                    ,
                    int.Parse(rotationStartDate.Substring(0,2))
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
                        var add = airtableBase.CreateRecord("People", fields);
                    }
                }

                var rotationLookup = airtableBase.ListRecords("Rotations", null, null, $"SEARCH(\"{rotation.Substring(0, 200)}\", {{Rotation text}}) > 0").GetAwaiter().GetResult();
                if (!rotationLookup.Records.Any())
                {
                    var fields = new Fields();
                    fields.AddField("Rotation #", rotationNumber);
                    // fields.AddField("Date", new DateTime(DateTime.Now.Year, , )));
                    var add = airtableBase.UpdateRecord("People", fields, rotationLookup.Records.First().Fields["Rotation ID"].ToString());
                }

            }

            return (ActionResult)new OkResult();
        }
    }
}
