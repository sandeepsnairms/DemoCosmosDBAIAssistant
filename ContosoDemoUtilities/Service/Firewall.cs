using Bogus.DataSets;
using NetFwTypeLib;
using Spectre.Console;
using System;
using System.Net;

namespace ContosoUtilities
{
    public class Firewall
    {
        public static void AddFirewallRule(string ruleName, string ipAddress)
        {
            try
            {
                INetFwRule firewallRule = (INetFwRule)Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwRule"));
                firewallRule.Action = NET_FW_ACTION_.NET_FW_ACTION_BLOCK;
                firewallRule.Description = "Block Multi-master";
                firewallRule.Direction = NET_FW_RULE_DIRECTION_.NET_FW_RULE_DIR_OUT;
                firewallRule.Enabled = true;
                firewallRule.InterfaceTypes = "All";
                firewallRule.Name = ruleName;
                //firewallRule.Protocol = 6;// NET_FW_IP_PROTOCOL_.NET_FW_IP_PROTOCOL_TCP;
                firewallRule.RemoteAddresses = ipAddress;

                INetFwPolicy2 firewallPolicy = (INetFwPolicy2)Activator.CreateInstance(
                    Type.GetTypeFromProgID("HNetCfg.FwPolicy2"));

                firewallPolicy.Rules.Add(firewallRule);

                AnsiConsole.MarkupLine($"Firewall rule added with name : \"{ruleName}\" for IP:{ipAddress}");

            }
            catch (Exception e)
            {

                AnsiConsole.MarkupLine($"Error: {e.Message}");
            }
        }


        public static void RemoveFirewallRule(string ruleName)
        {
            try
            {
                INetFwPolicy2 firewallPolicy = (INetFwPolicy2)Activator.CreateInstance(
                        Type.GetTypeFromProgID("HNetCfg.FwPolicy2"));
                firewallPolicy.Rules.Remove(ruleName);

                AnsiConsole.MarkupLine($"Removed firewall rule with name : \"{ruleName}\"");

            }
            catch (Exception e)
            {

                AnsiConsole.MarkupLine($"Error: {e.Message}");
            }
        }



    }
}
