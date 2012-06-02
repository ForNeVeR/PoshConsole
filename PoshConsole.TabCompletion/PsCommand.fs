module PoshConsole.TabCompletion.PsCommand

open System

let private escape (argument : string) =
    let needToEscape =
        argument
        |> Seq.exists (fun c -> Char.IsWhiteSpace(c) || c = '\'' || c = '"')

    if needToEscape
    then String.Format("\"{0}\"", argument.Replace("'", "`'").Replace("\"", "`\""))
    else argument

let formatSimple command args =
    String.Join(" ", [Seq.singleton command,
                      args |> Seq.map escape])

let formatTuples command args =
    String.Join(" ", [Seq.singleton command,
                      args |> Seq.map (fun name value -> [name, value])])
