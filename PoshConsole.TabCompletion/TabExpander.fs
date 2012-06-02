namespace PoshConsole.TabCompletion

open System
open System.Management.Automation
open System.Threading
open PoshConsole.PowerShell
open PoshConsole.PowerShell.Utilities

type TabExpander (host) =
    let runner = new CommandRunner(host)

    let invokeCommand command =
         let result = ref (new PipelineExecutionResult())
         let syncRoot = new AutoResetEvent(false)
         let command =
            new InputBoundCommand(
                [| command |], [],
                (fun r -> 
                    result := r
                    syncRoot.Set() |> ignore),
                AddToHistory = false, DefaultOutput = false)
            
         runner.Enqueue(command)
         syncRoot.WaitOne() |> ignore

         (!result).Output

    let getGeneralExpansions commandLine lastWord =
        invokeCommand <| PsCommand.formatTuples "TabExpansion" ["CmdLine", commandLine;
                                                                "LastWord", lastWord]
        |> Seq.map string

    let getCommandlets beginning =
        invokeCommand <| PsCommand.formatSimple "Get-Command" [(sprintf "%A*" beginning)]
        |> Seq.map (fun pso -> pso.ImmediateBaseObject)
        |> Seq.cast<CommandInfo>
        |> Seq.map (fun ci -> ci.Name)

    let getVariables beginning =
        invokeCommand <| PsCommand.formatSimple "Get-Variable" [(sprintf "%A*" beginning)]
        |> Seq.map (fun pso -> pso.ImmediateBaseObject)
        |> Seq.cast<PSVariable>
        |> Seq.map (fun psv -> sprintf "$%A" psv.Name)

    let getFiles path =
        invokeCommand <| PsCommand.formatSimple "Resolve-Path" [path]
        |> Seq.map (fun o -> o.ToString())

    member this.Expand (commandLine : string) =
        let lastWord = StringHelper.GetLastWord commandLine
        let general = getGeneralExpansions commandLine lastWord
        let commandlets = getCommandlets lastWord
        let variables = getVariables lastWord
        let files = getFiles (if lastWord = "" then ".\\" else lastWord)

        Seq.concat [general;
                    commandlets;
                    variables;
                    files]

    interface IDisposable with
        member this.Dispose() =
            runner.Dispose()