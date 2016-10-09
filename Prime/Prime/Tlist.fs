﻿// Prime - A PRIMitivEs code library.
// Copyright (C) Bryan Edds, 2012-2016.

namespace Prime
open System
open System.Collections.Generic

[<AutoOpen>]
module TlistModule =

    type [<NoEquality; NoComparison>] private 'a Log =
        | Add of 'a
        | Remove of 'a
        | Set of int * 'a

    type [<NoEquality; NoComparison>] 'a Tlist =
        private
            { mutable Tlist : 'a Tlist
              ImpList : 'a List
              ImpListOrigin : 'a List
              Logs : 'a Log list
              LogsLength : int
              BloatFactor : int }

        static member (>>.) (list : 'a2 Tlist, builder : Texpr<unit, 'a2 Tlist>) =
            (snd ^ builder list)

        static member (.>>) (list : 'a2 Tlist, builder : Texpr<'a2, 'a2 Tlist>) =
            (fst ^ builder list)

        static member (.>>.) (list : 'a2 Tlist, builder : Texpr<'a2, 'a2 Tlist>) =
            builder list

    let tlist<'a> = TexprBuilder<'a Tlist> ()

    [<RequireQualifiedAccess>]
    module Tlist =

        let private commit list =
            let oldList = list
            let impListOrigin = List<'a> list.ImpListOrigin
            List.foldBack (fun log () ->
                match log with
                | Add value -> impListOrigin.Add value
                | Remove value -> ignore ^ impListOrigin.Remove value
                | Set (index, value) -> impListOrigin.[index] <- value)
                list.Logs ()
            let impList = List<'a> impListOrigin
            let list = { list with ImpList = impList; ImpListOrigin = impListOrigin; Logs = []; LogsLength = 0 }
            list.Tlist <- list
            oldList.Tlist <- list
            list

        let private compress list =
            let oldList = list
            let impListOrigin = List<'a> list.ImpList
            let list = { list with ImpListOrigin = impListOrigin; Logs = []; LogsLength = 0 }
            list.Tlist <- list
            oldList.Tlist <- list
            list

        let private validate list =
            match obj.ReferenceEquals (list.Tlist, list) with
            | true -> if list.LogsLength > list.ImpList.Count * list.BloatFactor then compress list else list
            | false -> commit list

        let private update updater list =
            let oldList = list
            let list = validate list
            let list = updater list
            list.Tlist <- list
            oldList.Tlist <- list
            list

        let private makeFromTempList optBloatFactor (tempList : 'a List) =
            let list =
                { Tlist = Unchecked.defaultof<'a Tlist>
                  ImpList = tempList
                  ImpListOrigin = List<'a> tempList
                  Logs = []
                  LogsLength = 0
                  BloatFactor = Option.getOrDefault 1 optBloatFactor }
            list.Tlist <- list
            list

        let makeFromSeq optBloatFactor (items : 'a seq) =
            makeFromTempList optBloatFactor (List<'a> items)

        let makeEmpty<'a> optBloatFactor =
            makeFromSeq optBloatFactor (List<'a> ())

        let singleton item =
            makeFromSeq None (Seq.singleton item)

        let isEmpty list =
            let list = validate list
            (list.ImpList.Count = 0, list)

        let notEmpty list =
            let list = validate list
            mapFst not ^ isEmpty list

        let get index list =
            let list = validate list
            (list.ImpList.[index], list)

        let set index value list =
            update (fun list ->
                let list = { list with Logs = Set (index, value) :: list.Logs; LogsLength = list.LogsLength + 1 }
                list.ImpList.[index] <- value
                list)
                list

        let add value list =
            update (fun list ->
                let list = { list with Logs = Add value :: list.Logs; LogsLength = list.LogsLength + 1 }
                ignore ^ list.ImpList.Add value
                list)
                list

        let remove value list =
            update (fun list ->
                let list = { list with Logs = Remove value :: list.Logs; LogsLength = list.LogsLength + 1 }
                list.ImpList.Remove value |> ignore
                list)
                list

        /// Add all the given values to the list.
        let addMany values list =
            Seq.fold (flip add) list values

        /// Remove all the given values from the list.
        let removeMany values list =
            Seq.fold (flip remove) list values

        /// Get the length of the list (constant-time, obviously).
        let length list =
            let list = validate list
            (list.ImpList.Count, list)

        let contains value list =
            let list = validate list
            (list.ImpList.Contains value, list)

        /// Convert a Tlist to a seq. Note that entire list is iterated eagerly since the underlying .NET List could
        /// otherwise opaquely change during iteration.
        let toSeq list =
            let list = validate list
            let seq = list.ImpList |> Array.ofSeq :> 'a seq
            (seq, list)

        let ofSeq items =
            makeFromSeq None items

        let fold folder state list =
            let (seq, list) = toSeq list
            let result = Seq.fold folder state seq
            (result, list)

        let map (mapper : 'a -> 'b) (list : 'a Tlist) =
            // OPTIMIZATION: elides building of avoidable transactions.
            let list = validate list
            let impList = list.ImpList
            let tempList = List<'b> impList.Count
            for i in 0 .. impList.Count - 1 do tempList.Add ^ mapper impList.[i]
            let listMapped = makeFromTempList (Some list.BloatFactor) tempList
            (listMapped, list)

        let filter pred list =
            // OPTIMIZATION: elides building of avoidable transactions.
            let list = validate list
            let impList = list.ImpList
            let tempList = List<'a> impList.Count
            for i in 0 .. impList.Count - 1 do let item = impList.[i] in if pred item then tempList.Add item
            let listFiltered = makeFromTempList (Some list.BloatFactor) tempList
            (listFiltered, list)

        let rev list =
            // OPTIMIZATION: elides building of avoidable transactions.
            let list = validate list
            let impList = list.ImpList
            let tempList = List<'a> impList
            tempList.Reverse ()
            let listReversed = makeFromTempList (Some list.BloatFactor) tempList
            (listReversed, list)

        let sortWith comparison list =
            // OPTIMIZATION: elides building of avoidable transactions.
            let list = validate list
            let impList = list.ImpList
            let tempList = List<'b> impList
            let tempListSorted = Seq.sortWith comparison tempList // NOTE: Generic.List.Sort is _not_ stable, so using a stable one instead...
            let listSorted = makeFromSeq (Some list.BloatFactor) tempListSorted
            (listSorted, list)

        let sortBy by list =
            // OPTIMIZATION: elides building of avoidable transactions.
            let list = validate list
            let impList = list.ImpList
            let tempList = List<'b> impList.Count
            for i in 0 .. impList.Count - 1 do tempList.Add (by impList.[i])
            let tempListSorted = Seq.sort tempList // NOTE: Generic.List.Sort is _not_ stable, so using a stable one instead...
            let listSorted = makeFromSeq (Some list.BloatFactor) tempListSorted
            (listSorted, list)

        let sort list =
            // OPTIMIZATION: elides building of avoidable transactions.
            let list = validate list
            let impList = list.ImpList
            let tempList = List<'b> impList
            let tempListSorted = Seq.sort tempList // NOTE: Generic.List.Sort is _not_ stable, so using a stable one instead...
            let listSorted = makeFromSeq (Some list.BloatFactor) tempListSorted
            (listSorted, list)

        let concat lists =
            // OPTIMIZATION: elides building of avoidable transactions.
            let listsAsSeq = toSeq lists |> fst
            let tempList = List<'a> ()
            for list in listsAsSeq do tempList.AddRange (toSeq list |> fst)
            makeFromSeq None tempList

        let definitize list =
            let listMapped = filter Option.isSome list |> fst
            map Option.get listMapped

type 'a Tlist = 'a TlistModule.Tlist