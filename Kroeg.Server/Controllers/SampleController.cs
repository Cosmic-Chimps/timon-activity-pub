// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
// ------------------------------------------------------------

    using System;
    using System.Threading.Tasks;
    using Dapr;
    using Dapr.Client;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Logging;
    using Kroeg.Server.Tos.Request;
namespace Kroeg.Server.Controllers
{


    /// <summary>
    /// Sample showing Dapr integration with controller.
    /// </summary>
    [ApiController]
    public class SampleController : ControllerBase
    {
        /// <summary>
        /// SampleController Constructor with logger injection
        /// </summary>
        /// <param name="logger"></param>
        public SampleController(ILogger<SampleController> logger)
        {
            this.logger = logger;
        }

        /// <summary>
        /// State store name.
        /// </summary>
        public const string StoreName = "statestore";
        private readonly ILogger<SampleController> logger;


        /// <summary>
        /// Method for depositing to account as specified in transaction.
        /// </summary>
        /// <param name="transaction">Transaction info.</param>
        /// <param name="daprClient">State client to interact with Dapr runtime.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the result of the asynchronous operation.</returns>
        ///  "pubsub", the first parameter into the Topic attribute, is name of the default pub/sub configured by the Dapr CLI.
        [Topic("messagebus", "deposit")]
        [HttpPost("deposit")]
        public IActionResult Deposit(RegisterRequest transaction)
        {
            System.Console.WriteLine($"Enter deposit {transaction.Email}");
            return Ok(transaction);
        }
    }
}
