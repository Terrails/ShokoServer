﻿using System;
using System.Xml;
using Microsoft.Extensions.Logging;
using Shoko.Commons.Queue;
using Shoko.Models.Queue;
using Shoko.Server.Commands.Attributes;
using Shoko.Server.Commands.Generic;
using Shoko.Server.Server;

namespace Shoko.Server.Commands.AniDB;

[Serializable]
[Command(CommandRequestType.AniDB_UpdateMylistStats)]
[Obsolete("There's no point to this data for us. It's too much effort to maintain")]
public class CommandRequest_UpdateMyListStats : CommandRequestImplementation
{
    public override CommandRequestPriority DefaultPriority => CommandRequestPriority.Priority7;

    public override QueueStateStruct PrettyDescription => new()
    {
        message = "Updating AniDB MyList Stats",
        queueState = QueueStateEnum.UpdateMyListStats,
        extraParams = new string[0]
    };

    protected override void Process()
    {
        Logger.LogInformation("CommandRequest_UpdateMyListStats is deprecated. Skipping!");
    }

    public override void GenerateCommandID()
    {
        CommandID = "CommandRequest_UpdateMyListStats";
    }

    public override bool LoadFromCommandDetails()
    {
        // read xml to get parameters
        if (CommandDetails.Trim().Length <= 0) return false;

        var docCreator = new XmlDocument();
        docCreator.LoadXml(CommandDetails);

        return true;
    }

    public CommandRequest_UpdateMyListStats(ILoggerFactory loggerFactory) : base(loggerFactory)
    {
    }

    protected CommandRequest_UpdateMyListStats()
    {
    }
}
