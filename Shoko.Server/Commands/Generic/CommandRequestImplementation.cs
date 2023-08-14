﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Extensions.Logging;
using NHibernate;
using Shoko.Server.Commands.Attributes;
using Shoko.Server.Commands.Exceptions;
using Shoko.Server.Commands.Interfaces;
using Shoko.Server.Models;
using Shoko.Server.Repositories;
using Shoko.Server.Repositories.Direct;
using Shoko.Server.Server;
using ILoggerFactory = Microsoft.Extensions.Logging.ILoggerFactory;

namespace Shoko.Server.Commands.Generic;

public abstract class CommandRequestImplementation : CommandRequest, ICommandRequest
{
    [XmlIgnore][JsonIgnore] protected readonly ILogger Logger;

    // ignoring the base properties so that when we serialize we only get the properties
    // defined in the concrete class

    [XmlIgnore][JsonIgnore] public virtual bool BubbleExceptions { get; set; } = false;

    protected CommandRequestImplementation(ILoggerFactory loggerFactory) : this()
    {
        Logger = loggerFactory.CreateLogger(GetType());
    }

    [SuppressMessage("ReSharper", "VirtualMemberCallInConstructor")]
    protected CommandRequestImplementation()
    {
        CommandType = (int?)GetType().GetCustomAttribute<CommandAttribute>()?.RequestType ?? -1;
        Priority = (int)DefaultPriority;
    }

    /// <summary>
    /// Inherited classes to provide the implementation of how to process this command
    /// </summary>
    protected abstract void Process();

    public override void PostInit() { }

    public override void ProcessCommand()
    {
        try
        {
            Process();
        }
        catch (Exception e)
        {
            if (BubbleExceptions) throw;

            Logger.LogError(e, "Error processing {Type}: {CommandDetails}", GetType().Name, ToJson());
        }
    }

    public override CommandConflict ConflictBehavior => CommandConflict.Ignore;

    protected virtual string GetCommandDetails()
    {
        return ToXML();
    }

    private string ToXML()
    {
        var ns = new XmlSerializerNamespaces();
        ns.Add("", string.Empty);

        var serializer = new XmlSerializer(GetType());
        var settings = new XmlWriterSettings
        {
            OmitXmlDeclaration = true // Remove the <?xml version="1.0" encoding="utf-8"?>
        };
        var sb = new StringBuilder();
        var writer = XmlWriter.Create(sb, settings);
        serializer.Serialize(writer, this, ns);

        return sb.ToString();
    }

    private string ToJson()
    {
        return JsonSerializer.Serialize(this, GetType(), new JsonSerializerOptions
        {
            WriteIndented = false, MaxDepth = 5, IgnoreReadOnlyProperties = true, IncludeFields = false
        });
    }

    public virtual void Save(bool force = false)
    {
        var commandID = CommandID + (force ? "_Forced" : "");
        var crTemp = RepoFactory.CommandRequest.GetByCommandID(commandID);
        if (crTemp != null)
        {
            switch (ConflictBehavior)
            {
                case CommandConflict.Replace:
                    RepoFactory.CommandRequest.Delete(crTemp);
                    break;
                case CommandConflict.Ignore: return;
                case CommandConflict.Error:
                default: throw new CommandExistsException { CommandID = commandID };
            }
        }

        DateTimeUpdated = DateTime.Now;
        GenerateCommandID();
        CommandDetails = GetCommandDetails();
        Logger.LogTrace("Saving new CommandRequest: {CommandType} {CommandID}", (CommandRequestType)CommandType, CommandID);
        try
        {
            RepoFactory.CommandRequest.Save(this);
        }
        catch (TransactionException e)
        {
            Logger.LogError(e, "Failed to Save CommandRequest, retying");
            try
            {
                RepoFactory.CommandRequest.Save(this);
            }
            catch (TransactionException ex)
            {
                Logger.LogError(ex, "Still Failed to Save CommandRequest");
            }
        }

        switch (CommandRequestRepository.GetQueueIndex(this))
        {
            case 0:
                ShokoService.CmdProcessorGeneral.NotifyOfNewCommand();
                break;
            case 1:
                ShokoService.CmdProcessorHasher.NotifyOfNewCommand();
                break;
            case 2:
                ShokoService.CmdProcessorImages.NotifyOfNewCommand();
                break;
        }
    }

    protected static string TryGetProperty(XmlDocument doc, string keyName, string propertyName)
    {
        try
        {
            var keyTemp = keyName.ToLowerInvariant().Replace("_", "");
            var parent = doc?.Cast<XmlNode>()
                .FirstOrDefault(a => a.Name.ToLowerInvariant().Replace("_", "").Equals(keyTemp));
            if (parent == null)
            {
                return string.Empty;
            }

            var propName = propertyName.ToLowerInvariant().Replace("_", "");
            var prop = parent.Cast<XmlNode>()
                .FirstOrDefault(a => a.Name.ToLowerInvariant().Replace("_", "").Equals(propName))?.InnerText.Trim();
            return string.IsNullOrEmpty(prop) ? string.Empty : prop;
        }
        catch
        {
            //BaseConfig.MyAnimeLog.Write("---------------------------------------------------------------");
            //BaseConfig.MyAnimeLog.Write("Error in XMLService.TryGetProperty: {0}-{1}", Utils.GetParentMethodName(), ex.ToString());
            //BaseConfig.MyAnimeLog.Write("keyName: {0}, propertyName: {1}", keyName, propertyName);
            //BaseConfig.MyAnimeLog.Write("---------------------------------------------------------------");
        }

        return string.Empty;
    }

    protected static string TryGetProperty(XmlDocument doc, string keyName, params string[] propertyNames)
    {
        try
        {
            var keyTemp = keyName.ToLowerInvariant().Replace("_", "");
            var parent = doc?.Cast<XmlNode>()
                .FirstOrDefault(a => a.Name.ToLowerInvariant().Replace("_", "").Equals(keyTemp));
            if (parent == null)
            {
                return string.Empty;
            }

            foreach (var propertyName in propertyNames)
            {
                var propName = propertyName.ToLowerInvariant().Replace("_", "");
                var prop = parent.Cast<XmlNode>()
                    .FirstOrDefault(a => a.Name.ToLowerInvariant().Replace("_", "").Equals(propName))?.InnerText.Trim();
                if (string.IsNullOrEmpty(prop))
                {
                    continue;
                }

                return prop;
            }
        }
        catch
        {
            //BaseConfig.MyAnimeLog.Write("---------------------------------------------------------------");
            //BaseConfig.MyAnimeLog.Write("Error in XMLService.TryGetProperty: {0}-{1}", Utils.GetParentMethodName(), ex.ToString());
            //BaseConfig.MyAnimeLog.Write("keyName: {0}, propertyName: {1}", keyName, propertyName);
            //BaseConfig.MyAnimeLog.Write("---------------------------------------------------------------");
        }

        return string.Empty;
    }

    protected static string TryGetProperty(XmlDocument doc, string[] keyNames, params string[] propertyNames)
    {
        try
        {
            foreach (var keyName in keyNames)
            {
                var keyTemp = keyName.ToLowerInvariant().Replace("_", "");
                var parent = doc?.Cast<XmlNode>()
                    .FirstOrDefault(a => a.Name.ToLowerInvariant().Replace("_", "").Equals(keyTemp));
                if (parent == null)
                {
                    continue;
                }

                foreach (var propertyName in propertyNames)
                {
                    var propName = propertyName.ToLowerInvariant().Replace("_", "");
                    var prop = parent.Cast<XmlNode>()
                        .FirstOrDefault(a => a.Name.ToLowerInvariant().Replace("_", "").Equals(propName))?.InnerText
                        .Trim();
                    if (string.IsNullOrEmpty(prop))
                    {
                        continue;
                    }

                    return prop;
                }
            }
        }
        catch
        {
            //BaseConfig.MyAnimeLog.Write("---------------------------------------------------------------");
            //BaseConfig.MyAnimeLog.Write("Error in XMLService.TryGetProperty: {0}-{1}", Utils.GetParentMethodName(), ex.ToString());
            //BaseConfig.MyAnimeLog.Write("keyName: {0}, propertyName: {1}", keyName, propertyName);
            //BaseConfig.MyAnimeLog.Write("---------------------------------------------------------------");
        }

        return string.Empty;
    }
}
