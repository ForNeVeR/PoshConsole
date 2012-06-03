module PoshConsole.TabCompletion.PsCommand

open System

let private escape (argument : string) =
    let needToEscape =
        argument
        |> Seq.exists (fun c -> Char.IsWhiteSpace(c) || c = '\'' || c = '"')

    if needToEscape
    then String.Format("\"{0}\"", argument.Replace("'", "`'").Replace("\"", "`\""))
    else argument

let formatSimple (command : string) (args : string seq) =
    String.Join(" ", command :: (args
                                 |> Seq.map escape
                                 |> Seq.toList))

let formatTuples (command : string) (args : (string * string) seq) =
    String.Join(" ", command :: (args
                                 |> Seq.map (function name, value -> [name; escape value])
                                 |> Seq.concat
                                 |> Seq.toList))
