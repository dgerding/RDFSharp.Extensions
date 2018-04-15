﻿/*
   Copyright 2012-2017 Marco De Salvo

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

     http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Npgsql;
using NpgsqlTypes;
using RDFSharp.Model;
using RDFSharp.Query;

namespace RDFSharp.Store {

    /// <summary>
    /// RDFPostgreSQLStore represents a store backed on PostgreSQL engine
    /// </summary>
    public class RDFPostgreSQLStore: RDFStore {

        #region Properties
        /// <summary>
        /// Connection to the PostgreSQL database
        /// </summary>
        internal NpgsqlConnection Connection { get; set; }
        #endregion

        #region Ctors
        /// <summary>
        /// Default-ctor to build a PostgreSQL store instance with the given credentials
        /// </summary>
        public RDFPostgreSQLStore(String pgsqlInstance,
                                  String pgsqlDatabase,
                                  String pgsqlUser,
                                  String pgsqlPassword) {
            if (pgsqlInstance              != null) {
                if (pgsqlDatabase          != null) {
                    if (pgsqlUser          != null) {
                        if (pgsqlPassword  != null) {

                            //Initialize store structures
                            this.StoreType  = "POSTGRESQL";
                            this.Connection = new NpgsqlConnection(@"Server="    + pgsqlInstance +
                                                                    ";Database=" + pgsqlDatabase +
                                                                    ";User Id="  + pgsqlUser     +
                                                                    ";Password=" + pgsqlPassword +
                                                                    ";Port=5432;");
                            this.StoreID    = RDFModelUtilities.CreateHash(this.ToString());

                            //Perform initial diagnostics
                            this.PrepareStore();

                        }
                        else {
                            throw new RDFStoreException("Cannot connect to PostgreSQL store because: given \"pgsqlPassword\" parameter is null.");
                        }
                    }
                    else {
                        throw new RDFStoreException("Cannot connect to PostgreSQL store because: given \"pgsqlUser\" parameter is null.");
                    }
                }
                else {
                    throw new RDFStoreException("Cannot connect to PostgreSQL store because: given \"pgsqlDatabase\" parameter is null.");
                }
            }
            else {
                throw new RDFStoreException("Cannot connect to PostgreSQL store because: given \"pgsqlInstance\" parameter is null.");
            }
        }
        #endregion

        #region Interfaces
        /// <summary>
        /// Gives the string representation of the PostgreSQL store 
        /// </summary>
        public override String ToString() {
            return base.ToString() + "|SERVER=" + this.Connection.DataSource + ";DATABASE=" + this.Connection.Database;
        }
        #endregion

        #region Methods

        #region Add
        /// <summary>
        /// Merges the given graph into the store within a single transaction, avoiding duplicate insertions
        /// </summary>
        public override RDFStore MergeGraph(RDFGraph graph) {
            if (graph       != null) {
                var graphCtx = new RDFContext(graph.Context);

                //Create command
                var command  = new NpgsqlCommand("INSERT INTO public.\"quadruples\"(quadrupleid, tripleflavor, context, contextid, subject, subjectid, predicate, predicateid, object, objectid) SELECT @QID, @TFV, @CTX, @CTXID, @SUBJ, @SUBJID, @PRED, @PREDID, @OBJ, @OBJID WHERE NOT EXISTS (SELECT 1 FROM public.\"quadruples\" WHERE quadrupleid = @QID)", this.Connection);
                command.Parameters.Add(new NpgsqlParameter("QID",    NpgsqlDbType.Bigint));
                command.Parameters.Add(new NpgsqlParameter("TFV",    NpgsqlDbType.Integer));
                command.Parameters.Add(new NpgsqlParameter("CTX",    NpgsqlDbType.Varchar, 1000));
                command.Parameters.Add(new NpgsqlParameter("CTXID",  NpgsqlDbType.Bigint));
                command.Parameters.Add(new NpgsqlParameter("SUBJ",   NpgsqlDbType.Varchar, 1000));
                command.Parameters.Add(new NpgsqlParameter("SUBJID", NpgsqlDbType.Bigint));
                command.Parameters.Add(new NpgsqlParameter("PRED",   NpgsqlDbType.Varchar, 1000));
                command.Parameters.Add(new NpgsqlParameter("PREDID", NpgsqlDbType.Bigint));
                command.Parameters.Add(new NpgsqlParameter("OBJ",    NpgsqlDbType.Varchar, 1000));
                command.Parameters.Add(new NpgsqlParameter("OBJID",  NpgsqlDbType.Bigint));

                try {

                    //Open connection
                    this.Connection.Open();

                    //Prepare command
                    command.Prepare();

                    //Open transaction
                    command.Transaction = this.Connection.BeginTransaction();

                    //Iterate triples
                    foreach(var triple in graph) {

                        //Valorize parameters
                        command.Parameters["QID"].Value    = RDFModelUtilities.CreateHash(graphCtx         + " " +
                                                                                          triple.Subject   + " " +
                                                                                          triple.Predicate + " " +
                                                                                          triple.Object);
                        command.Parameters["TFV"].Value    = triple.TripleFlavor;
                        command.Parameters["CTX"].Value    = graphCtx.ToString();
                        command.Parameters["CTXID"].Value  = graphCtx.PatternMemberID;
                        command.Parameters["SUBJ"].Value   = triple.Subject.ToString();
                        command.Parameters["SUBJID"].Value = triple.Subject.PatternMemberID;
                        command.Parameters["PRED"].Value   = triple.Predicate.ToString();
                        command.Parameters["PREDID"].Value = triple.Predicate.PatternMemberID;
                        command.Parameters["OBJ"].Value    = triple.Object.ToString();
                        command.Parameters["OBJID"].Value  = triple.Object.PatternMemberID;

                        //Execute command
                        command.ExecuteNonQuery();
                    }

                    //Close transaction
                    command.Transaction.Commit();

                    //Close connection
                    this.Connection.Close();

                }
                catch (Exception ex) {

                    //Rollback transaction
                    command.Transaction.Rollback();

                    //Close connection
                    this.Connection.Close();

                    //Propagate exception
                    throw new RDFStoreException("Cannot insert data into PostgreSQL store because: " + ex.Message, ex);

                }

            }
            return this;
        }

        /// <summary>
        /// Adds the given quadruple to the store, avoiding duplicate insertions
        /// </summary>
        public override RDFStore AddQuadruple(RDFQuadruple quadruple) {
            if (quadruple  != null) {

                //Create command
                var command = new NpgsqlCommand("INSERT INTO public.\"quadruples\"(quadrupleid, tripleflavor, context, contextid, subject, subjectid, predicate, predicateid, object, objectid) SELECT @QID, @TFV, @CTX, @CTXID, @SUBJ, @SUBJID, @PRED, @PREDID, @OBJ, @OBJID WHERE NOT EXISTS (SELECT 1 FROM public.\"quadruples\" WHERE quadrupleid = @QID)", this.Connection);
                command.Parameters.Add(new NpgsqlParameter("QID",    NpgsqlDbType.Bigint));
                command.Parameters.Add(new NpgsqlParameter("TFV",    NpgsqlDbType.Integer));
                command.Parameters.Add(new NpgsqlParameter("CTX",    NpgsqlDbType.Varchar, 1000));
                command.Parameters.Add(new NpgsqlParameter("CTXID",  NpgsqlDbType.Bigint));
                command.Parameters.Add(new NpgsqlParameter("SUBJ",   NpgsqlDbType.Varchar, 1000));
                command.Parameters.Add(new NpgsqlParameter("SUBJID", NpgsqlDbType.Bigint));
                command.Parameters.Add(new NpgsqlParameter("PRED",   NpgsqlDbType.Varchar, 1000));
                command.Parameters.Add(new NpgsqlParameter("PREDID", NpgsqlDbType.Bigint));
                command.Parameters.Add(new NpgsqlParameter("OBJ",    NpgsqlDbType.Varchar, 1000));
                command.Parameters.Add(new NpgsqlParameter("OBJID",  NpgsqlDbType.Bigint));

                //Valorize parameters
                command.Parameters["QID"].Value    = quadruple.QuadrupleID;
                command.Parameters["TFV"].Value    = quadruple.TripleFlavor;
                command.Parameters["CTX"].Value    = quadruple.Context.ToString();
                command.Parameters["CTXID"].Value  = quadruple.Context.PatternMemberID;
                command.Parameters["SUBJ"].Value   = quadruple.Subject.ToString();
                command.Parameters["SUBJID"].Value = quadruple.Subject.PatternMemberID;
                command.Parameters["PRED"].Value   = quadruple.Predicate.ToString();
                command.Parameters["PREDID"].Value = quadruple.Predicate.PatternMemberID;
                command.Parameters["OBJ"].Value    = quadruple.Object.ToString();
                command.Parameters["OBJID"].Value  = quadruple.Object.PatternMemberID;

                try {

                    //Open connection
                    this.Connection.Open();

                    //Prepare command
                    command.Prepare();

                    //Open transaction
                    command.Transaction = this.Connection.BeginTransaction();

                    //Execute command
                    command.ExecuteNonQuery();

                    //Close transaction
                    command.Transaction.Commit();

                    //Close connection
                    this.Connection.Close();

                }
                catch (Exception ex) {

                    //Rollback transaction
                    command.Transaction.Rollback();

                    //Close connection
                    this.Connection.Close();

                    //Propagate exception
                    throw new RDFStoreException("Cannot insert data into PostgreSQL store because: " + ex.Message, ex);

                }

            }
            return this;
        }
        #endregion

        #region Remove
        /// <summary>
        /// Removes the given quadruples from the store
        /// </summary>
        public override RDFStore RemoveQuadruple(RDFQuadruple quadruple) {
            if (quadruple  != null) {

                //Create command
                var command = new NpgsqlCommand("DELETE FROM public.\"quadruples\" WHERE quadrupleid = @QID", this.Connection);
                command.Parameters.Add(new NpgsqlParameter("QID", NpgsqlDbType.Bigint));

                //Valorize parameters
                command.Parameters["QID"].Value = quadruple.QuadrupleID;

                try {

                    //Open connection
                    this.Connection.Open();

                    //Prepare command
                    command.Prepare();

                    //Open transaction
                    command.Transaction = this.Connection.BeginTransaction();

                    //Execute command
                    command.ExecuteNonQuery();

                    //Close transaction
                    command.Transaction.Commit();

                    //Close connection
                    this.Connection.Close();

                }
                catch (Exception ex) {

                    //Rollback transaction
                    command.Transaction.Rollback();

                    //Close connection
                    this.Connection.Close();

                    //Propagate exception
                    throw new RDFStoreException("Cannot delete data from PostgreSQL store because: " + ex.Message, ex);

                }

            }
            return this;
        }

        /// <summary>
        /// Removes the quadruples with the given context
        /// </summary>
        public override RDFStore RemoveQuadruplesByContext(RDFContext contextResource) {
            if (contextResource != null) {

                //Create command
                var command      = new NpgsqlCommand("DELETE FROM public.\"quadruples\" WHERE contextid = @CTXID", this.Connection);
                command.Parameters.Add(new NpgsqlParameter("CTXID", NpgsqlDbType.Bigint));

                //Valorize parameters
                command.Parameters["CTXID"].Value = contextResource.PatternMemberID;

                try {

                    //Open connection
                    this.Connection.Open();

                    //Prepare command
                    command.Prepare();

                    //Open transaction
                    command.Transaction = this.Connection.BeginTransaction();

                    //Execute command
                    command.ExecuteNonQuery();

                    //Close transaction
                    command.Transaction.Commit();

                    //Close connection
                    this.Connection.Close();

                }
                catch (Exception ex) {

                    //Rollback transaction
                    command.Transaction.Rollback();

                    //Close connection
                    this.Connection.Close();

                    //Propagate exception
                    throw new RDFStoreException("Cannot delete data from PostgreSQL store because: " + ex.Message, ex);

                }

            }
            return this;
        }

        /// <summary>
        /// Removes the quadruples with the given subject
        /// </summary>
        public override RDFStore RemoveQuadruplesBySubject(RDFResource subjectResource) {
            if (subjectResource != null) {

                //Create command
                var command      = new NpgsqlCommand("DELETE FROM public.\"quadruples\" WHERE subjectid = @SUBJID", this.Connection);
                command.Parameters.Add(new NpgsqlParameter("SUBJID", NpgsqlDbType.Bigint));

                //Valorize parameters
                command.Parameters["SUBJID"].Value = subjectResource.PatternMemberID;

                try {

                    //Open connection
                    this.Connection.Open();

                    //Prepare command
                    command.Prepare();

                    //Open transaction
                    command.Transaction = this.Connection.BeginTransaction();

                    //Execute command
                    command.ExecuteNonQuery();

                    //Close transaction
                    command.Transaction.Commit();

                    //Close connection
                    this.Connection.Close();

                }
                catch (Exception ex) {

                    //Rollback transaction
                    command.Transaction.Rollback();

                    //Close connection
                    this.Connection.Close();

                    //Propagate exception
                    throw new RDFStoreException("Cannot delete data from PostgreSQL store because: " + ex.Message, ex);

                }

            }
            return this;
        }

        /// <summary>
        /// Removes the quadruples with the given predicate
        /// </summary>
        public override RDFStore RemoveQuadruplesByPredicate(RDFResource predicateResource) {
            if (predicateResource != null) {

                //Create command
                var command        = new NpgsqlCommand("DELETE FROM public.\"quadruples\" WHERE predicateid = @PREDID", this.Connection);
                command.Parameters.Add(new NpgsqlParameter("PREDID", NpgsqlDbType.Bigint));

                //Valorize parameters
                command.Parameters["PREDID"].Value = predicateResource.PatternMemberID;

                try {

                    //Open connection
                    this.Connection.Open();

                    //Prepare command
                    command.Prepare();

                    //Open transaction
                    command.Transaction = this.Connection.BeginTransaction();

                    //Execute command
                    command.ExecuteNonQuery();

                    //Close transaction
                    command.Transaction.Commit();

                    //Close connection
                    this.Connection.Close();

                }
                catch (Exception ex) {

                    //Rollback transaction
                    command.Transaction.Rollback();

                    //Close connection
                    this.Connection.Close();

                    //Propagate exception
                    throw new RDFStoreException("Cannot delete data from PostgreSQL store because: " + ex.Message, ex);

                }

            }
            return this;
        }

        /// <summary>
        /// Removes the quadruples with the given resource as object
        /// </summary>
        public override RDFStore RemoveQuadruplesByObject(RDFResource objectResource) {
            if (objectResource != null) {

                //Create command
                var command     = new NpgsqlCommand("DELETE FROM public.\"quadruples\" WHERE objectid = @OBJID AND tripleflavor = @TFV", this.Connection);
                command.Parameters.Add(new NpgsqlParameter("OBJID", NpgsqlDbType.Bigint));
                command.Parameters.Add(new NpgsqlParameter("TFV",   NpgsqlDbType.Integer));

                //Valorize parameters
                command.Parameters["OBJID"].Value = objectResource.PatternMemberID;
                command.Parameters["TFV"].Value   = RDFModelEnums.RDFTripleFlavors.SPO;

                try {

                    //Open connection
                    this.Connection.Open();

                    //Prepare command
                    command.Prepare();

                    //Open transaction
                    command.Transaction = this.Connection.BeginTransaction();

                    //Execute command
                    command.ExecuteNonQuery();

                    //Close transaction
                    command.Transaction.Commit();

                    //Close connection
                    this.Connection.Close();

                }
                catch (Exception ex) {

                    //Rollback transaction
                    command.Transaction.Rollback();

                    //Close connection
                    this.Connection.Close();

                    //Propagate exception
                    throw new RDFStoreException("Cannot delete data from PostgreSQL store because: " + ex.Message, ex);

                }

            }
            return this;
        }

        /// <summary>
        /// Removes the quadruples with the given literal as object
        /// </summary>
        public override RDFStore RemoveQuadruplesByLiteral(RDFLiteral literalObject) {
            if (literalObject != null) {

                //Create command
                var command    = new NpgsqlCommand("DELETE FROM public.\"quadruples\" WHERE objectid = @OBJID AND tripleflavor = @TFV", this.Connection);
                command.Parameters.Add(new NpgsqlParameter("OBJID", NpgsqlDbType.Bigint));
                command.Parameters.Add(new NpgsqlParameter("TFV",   NpgsqlDbType.Integer));

                //Valorize parameters
                command.Parameters["OBJID"].Value = literalObject.PatternMemberID;
                command.Parameters["TFV"].Value   = RDFModelEnums.RDFTripleFlavors.SPL;

                try {

                    //Open connection
                    this.Connection.Open();

                    //Prepare command
                    command.Prepare();

                    //Open transaction
                    command.Transaction = this.Connection.BeginTransaction();

                    //Execute command
                    command.ExecuteNonQuery();

                    //Close transaction
                    command.Transaction.Commit();

                    //Close connection
                    this.Connection.Close();

                }
                catch (Exception ex) {

                    //Rollback transaction
                    command.Transaction.Rollback();

                    //Close connection
                    this.Connection.Close();

                    //Propagate exception
                    throw new RDFStoreException("Cannot delete data from PostgreSQL store because: " + ex.Message, ex);

                }

            }
            return this;
        }

        /// <summary>
        /// Clears the quadruples of the store
        /// </summary>
        public override void ClearQuadruples() {

            //Create command
            var command = new NpgsqlCommand("DELETE FROM public.\"quadruples\"", this.Connection);

            try {

                //Open connection
                this.Connection.Open();

                //Prepare command
                command.Prepare();

                //Open transaction
                command.Transaction = this.Connection.BeginTransaction();

                //Execute command
                command.ExecuteNonQuery();

                //Close transaction
                command.Transaction.Commit();

                //Close connection
                this.Connection.Close();

            }
            catch (Exception ex) {

                //Rollback transaction
                command.Transaction.Rollback();

                //Close connection
                this.Connection.Close();

                //Propagate exception
                throw new RDFStoreException("Cannot delete data from PostgreSQL store because: " + ex.Message, ex);

            }

        }
        #endregion

        #region Select
        /// <summary>
        /// Gets a memory store containing quadruples satisfying the given pattern
        /// </summary>
        internal override RDFMemoryStore SelectQuadruples(RDFContext  ctx,
                                                          RDFResource subj,
                                                          RDFResource pred,
                                                          RDFResource obj,
                                                          RDFLiteral  lit) {
            RDFMemoryStore result    = new RDFMemoryStore();
            NpgsqlCommand command    = null;

            //Intersect the filters
            if (ctx                 != null) {
                if (subj            != null) {
                    if (pred        != null) {
                        if (obj     != null) {
                            //C->S->P->O
                            command  = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE contextid = @CTXID AND subjectid = @SUBJID AND predicateid = @PREDID AND objectid = @OBJID AND tripleflavor = @TFV", this.Connection);
                            command.Parameters.Add(new NpgsqlParameter("TFV",    NpgsqlDbType.Integer));
                            command.Parameters.Add(new NpgsqlParameter("CTXID",  NpgsqlDbType.Bigint));
                            command.Parameters.Add(new NpgsqlParameter("SUBJID", NpgsqlDbType.Bigint));
                            command.Parameters.Add(new NpgsqlParameter("PREDID", NpgsqlDbType.Bigint));
                            command.Parameters.Add(new NpgsqlParameter("OBJID",  NpgsqlDbType.Bigint));
                            command.Parameters["TFV"].Value    = RDFModelEnums.RDFTripleFlavors.SPO;
                            command.Parameters["CTXID"].Value  = ctx.PatternMemberID;
                            command.Parameters["SUBJID"].Value = subj.PatternMemberID;
                            command.Parameters["PREDID"].Value = pred.PatternMemberID;
                            command.Parameters["OBJID"].Value  = obj.PatternMemberID;
                        }
                        else {
                            if (lit != null) {
                                //C->S->P->L
                                command = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE contextid = @CTXID AND subjectid = @SUBJID AND predicateid = @PREDID AND objectid = @OBJID AND tripleflavor = @TFV", this.Connection);
                                command.Parameters.Add(new NpgsqlParameter("TFV",    NpgsqlDbType.Integer));
                                command.Parameters.Add(new NpgsqlParameter("CTXID",  NpgsqlDbType.Bigint));
                                command.Parameters.Add(new NpgsqlParameter("SUBJID", NpgsqlDbType.Bigint));
                                command.Parameters.Add(new NpgsqlParameter("PREDID", NpgsqlDbType.Bigint));
                                command.Parameters.Add(new NpgsqlParameter("OBJID",  NpgsqlDbType.Bigint));
                                command.Parameters["TFV"].Value    = RDFModelEnums.RDFTripleFlavors.SPL;
                                command.Parameters["CTXID"].Value  = ctx.PatternMemberID;
                                command.Parameters["SUBJID"].Value = subj.PatternMemberID;
                                command.Parameters["PREDID"].Value = pred.PatternMemberID;
                                command.Parameters["OBJID"].Value  = lit.PatternMemberID;
                            }
                            else {
                                //C->S->P->
                                command = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE contextid = @CTXID AND subjectid = @SUBJID AND predicateid = @PREDID", this.Connection);
                                command.Parameters.Add(new NpgsqlParameter("CTXID",  NpgsqlDbType.Bigint));
                                command.Parameters.Add(new NpgsqlParameter("SUBJID", NpgsqlDbType.Bigint));
                                command.Parameters.Add(new NpgsqlParameter("PREDID", NpgsqlDbType.Bigint));
                                command.Parameters["CTXID"].Value  = ctx.PatternMemberID;
                                command.Parameters["SUBJID"].Value = subj.PatternMemberID;
                                command.Parameters["PREDID"].Value = pred.PatternMemberID;
                            }
                        }
                    }
                    else {
                        if (obj     != null) {
                            //C->S->->O
                            command  = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE contextid = @CTXID AND subjectid = @SUBJID AND objectid = @OBJID AND tripleflavor = @TFV", this.Connection);
                            command.Parameters.Add(new NpgsqlParameter("TFV",    NpgsqlDbType.Integer));
                            command.Parameters.Add(new NpgsqlParameter("CTXID",  NpgsqlDbType.Bigint));
                            command.Parameters.Add(new NpgsqlParameter("SUBJID", NpgsqlDbType.Bigint));
                            command.Parameters.Add(new NpgsqlParameter("OBJID",  NpgsqlDbType.Bigint));
                            command.Parameters["TFV"].Value    = RDFModelEnums.RDFTripleFlavors.SPO;
                            command.Parameters["CTXID"].Value  = ctx.PatternMemberID;
                            command.Parameters["SUBJID"].Value = subj.PatternMemberID;
                            command.Parameters["OBJID"].Value  = obj.PatternMemberID;
                        }
                        else {
                            if (lit != null) {
                                //C->S->->L
                                command = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE contextid = @CTXID AND subjectid = @SUBJID AND objectid = @OBJID AND tripleflavor = @TFV", this.Connection);
                                command.Parameters.Add(new NpgsqlParameter("TFV",    NpgsqlDbType.Integer));
                                command.Parameters.Add(new NpgsqlParameter("CTXID",  NpgsqlDbType.Bigint));
                                command.Parameters.Add(new NpgsqlParameter("SUBJID", NpgsqlDbType.Bigint));
                                command.Parameters.Add(new NpgsqlParameter("OBJID",  NpgsqlDbType.Bigint));
                                command.Parameters["TFV"].Value    = RDFModelEnums.RDFTripleFlavors.SPL;
                                command.Parameters["CTXID"].Value  = ctx.PatternMemberID;
                                command.Parameters["SUBJID"].Value = subj.PatternMemberID;
                                command.Parameters["OBJID"].Value  = lit.PatternMemberID;
                            }
                            else {
                                //C->S->->
                                command = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE contextid = @CTXID AND subjectid = @SUBJID", this.Connection);
                                command.Parameters.Add(new NpgsqlParameter("CTXID",  NpgsqlDbType.Bigint));
                                command.Parameters.Add(new NpgsqlParameter("SUBJID", NpgsqlDbType.Bigint));
                                command.Parameters["CTXID"].Value  = ctx.PatternMemberID;
                                command.Parameters["SUBJID"].Value = subj.PatternMemberID;
                            }
                        }
                    }
                }
                else {
                    if (pred        != null) {
                        if (obj     != null) {
                            //C->->P->O
                            command  = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE contextid = @CTXID AND predicateid = @PREDID AND objectid = @OBJID AND tripleflavor = @TFV", this.Connection);
                            command.Parameters.Add(new NpgsqlParameter("TFV",    NpgsqlDbType.Integer));
                            command.Parameters.Add(new NpgsqlParameter("CTXID",  NpgsqlDbType.Bigint));
                            command.Parameters.Add(new NpgsqlParameter("PREDID", NpgsqlDbType.Bigint));
                            command.Parameters.Add(new NpgsqlParameter("OBJID",  NpgsqlDbType.Bigint));
                            command.Parameters["TFV"].Value    = RDFModelEnums.RDFTripleFlavors.SPO;
                            command.Parameters["CTXID"].Value  = ctx.PatternMemberID;
                            command.Parameters["PREDID"].Value = pred.PatternMemberID;
                            command.Parameters["OBJID"].Value  = obj.PatternMemberID;
                        }
                        else {
                            if (lit != null) {
                                //C->->P->L
                                command = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE contextid = @CTXID AND predicateid = @PREDID AND objectid = @OBJID AND tripleflavor = @TFV", this.Connection);
                                command.Parameters.Add(new NpgsqlParameter("TFV",    NpgsqlDbType.Integer));
                                command.Parameters.Add(new NpgsqlParameter("CTXID",  NpgsqlDbType.Bigint));
                                command.Parameters.Add(new NpgsqlParameter("PREDID", NpgsqlDbType.Bigint));
                                command.Parameters.Add(new NpgsqlParameter("OBJID",  NpgsqlDbType.Bigint));
                                command.Parameters["TFV"].Value    = RDFModelEnums.RDFTripleFlavors.SPL;
                                command.Parameters["CTXID"].Value  = ctx.PatternMemberID;
                                command.Parameters["PREDID"].Value = pred.PatternMemberID;
                                command.Parameters["OBJID"].Value  = lit.PatternMemberID;
                            }
                            else {
                                //C->->P->
                                command = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE contextid = @CTXID AND predicateid = @PREDID", this.Connection);
                                command.Parameters.Add(new NpgsqlParameter("CTXID",  NpgsqlDbType.Bigint));
                                command.Parameters.Add(new NpgsqlParameter("PREDID", NpgsqlDbType.Bigint));
                                command.Parameters["CTXID"].Value  = ctx.PatternMemberID;
                                command.Parameters["PREDID"].Value = pred.PatternMemberID;
                            }
                        }
                    }
                    else {
                        if (obj     != null) {
                            //C->->->O
                            command  = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE contextid = @CTXID AND objectid = @OBJID AND tripleflavor = @TFV", this.Connection);
                            command.Parameters.Add(new NpgsqlParameter("TFV",   NpgsqlDbType.Integer));
                            command.Parameters.Add(new NpgsqlParameter("CTXID", NpgsqlDbType.Bigint));
                            command.Parameters.Add(new NpgsqlParameter("OBJID", NpgsqlDbType.Bigint));
                            command.Parameters["TFV"].Value   = RDFModelEnums.RDFTripleFlavors.SPO;
                            command.Parameters["CTXID"].Value = ctx.PatternMemberID;
                            command.Parameters["OBJID"].Value = obj.PatternMemberID;
                        }
                        else {
                            if (lit != null) {
                                //C->->->L
                                command = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE contextid = @CTXID AND objectid = @OBJID AND tripleflavor = @TFV", this.Connection);
                                command.Parameters.Add(new NpgsqlParameter("TFV",   NpgsqlDbType.Integer));
                                command.Parameters.Add(new NpgsqlParameter("CTXID", NpgsqlDbType.Bigint));
                                command.Parameters.Add(new NpgsqlParameter("OBJID", NpgsqlDbType.Bigint));
                                command.Parameters["TFV"].Value   = RDFModelEnums.RDFTripleFlavors.SPL;
                                command.Parameters["CTXID"].Value = ctx.PatternMemberID;
                                command.Parameters["OBJID"].Value = lit.PatternMemberID;
                            }
                            else {
                                //C->->->
                                command = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE contextid = @CTXID", this.Connection);
                                command.Parameters.Add(new NpgsqlParameter("CTXID", NpgsqlDbType.Bigint));
                                command.Parameters["CTXID"].Value = ctx.PatternMemberID;
                            }
                        }
                    }
                }
            }
            else {
                if (subj            != null) {
                    if (pred        != null) {
                        if (obj     != null) {
                            //->S->P->O
                            command  = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE subjectid = @SUBJID AND predicateid = @PREDID AND objectid = @OBJID AND tripleflavor = @TFV", this.Connection);
                            command.Parameters.Add(new NpgsqlParameter("TFV",    NpgsqlDbType.Integer));
                            command.Parameters.Add(new NpgsqlParameter("SUBJID", NpgsqlDbType.Bigint));
                            command.Parameters.Add(new NpgsqlParameter("PREDID", NpgsqlDbType.Bigint));
                            command.Parameters.Add(new NpgsqlParameter("OBJID",  NpgsqlDbType.Bigint));
                            command.Parameters["TFV"].Value    = RDFModelEnums.RDFTripleFlavors.SPO;
                            command.Parameters["SUBJID"].Value = subj.PatternMemberID;
                            command.Parameters["PREDID"].Value = pred.PatternMemberID;
                            command.Parameters["OBJID"].Value  = obj.PatternMemberID;
                        }
                        else {
                            if (lit != null) {
                                //->S->P->L
                                command = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE subjectid = @SUBJID AND predicateid = @PREDID AND objectid = @OBJID AND tripleflavor = @TFV", this.Connection);
                                command.Parameters.Add(new NpgsqlParameter("TFV",    NpgsqlDbType.Integer));
                                command.Parameters.Add(new NpgsqlParameter("SUBJID", NpgsqlDbType.Bigint));
                                command.Parameters.Add(new NpgsqlParameter("PREDID", NpgsqlDbType.Bigint));
                                command.Parameters.Add(new NpgsqlParameter("OBJID",  NpgsqlDbType.Bigint));
                                command.Parameters["TFV"].Value    = RDFModelEnums.RDFTripleFlavors.SPL;
                                command.Parameters["SUBJID"].Value = subj.PatternMemberID;
                                command.Parameters["PREDID"].Value = pred.PatternMemberID;
                                command.Parameters["OBJID"].Value  = lit.PatternMemberID;
                            }
                            else {
                                //->S->P->
                                command = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE subjectid = @SUBJID AND predicateid = @PREDID", this.Connection);
                                command.Parameters.Add(new NpgsqlParameter("SUBJID", NpgsqlDbType.Bigint));
                                command.Parameters.Add(new NpgsqlParameter("PREDID", NpgsqlDbType.Bigint));
                                command.Parameters["SUBJID"].Value = subj.PatternMemberID;
                                command.Parameters["PREDID"].Value = pred.PatternMemberID;
                            }
                        }
                    }
                    else {
                        if (obj     != null) {
                            //->S->->O
                            command  = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE subjectid = @SUBJID AND objectid = @OBJID AND tripleflavor = @TFV", this.Connection);
                            command.Parameters.Add(new NpgsqlParameter("TFV",    NpgsqlDbType.Integer));
                            command.Parameters.Add(new NpgsqlParameter("SUBJID", NpgsqlDbType.Bigint));
                            command.Parameters.Add(new NpgsqlParameter("OBJID",  NpgsqlDbType.Bigint));
                            command.Parameters["TFV"].Value    = RDFModelEnums.RDFTripleFlavors.SPO;
                            command.Parameters["SUBJID"].Value = subj.PatternMemberID;
                            command.Parameters["OBJID"].Value  = obj.PatternMemberID;
                        }
                        else {
                            if (lit != null) {
                                //->S->->L
                                command = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE subjectid = @SUBJID AND objectid = @OBJID AND tripleflavor = @TFV", this.Connection);
                                command.Parameters.Add(new NpgsqlParameter("TFV",    NpgsqlDbType.Integer));
                                command.Parameters.Add(new NpgsqlParameter("SUBJID", NpgsqlDbType.Bigint));
                                command.Parameters.Add(new NpgsqlParameter("OBJID",  NpgsqlDbType.Bigint));
                                command.Parameters["TFV"].Value    = RDFModelEnums.RDFTripleFlavors.SPL;
                                command.Parameters["SUBJID"].Value = subj.PatternMemberID;
                                command.Parameters["OBJID"].Value  = lit.PatternMemberID;
                            }
                            else {
                                //->S->->
                                command = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE subjectid = @SUBJID", this.Connection);
                                command.Parameters.Add(new NpgsqlParameter("SUBJID", NpgsqlDbType.Bigint));
                                command.Parameters["SUBJID"].Value = subj.PatternMemberID;
                            }
                        }
                    }
                }
                else {
                    if (pred        != null) {
                        if (obj     != null) {
                            //->->P->O
                            command  = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE predicateid = @PREDID AND objectid = @OBJID AND tripleflavor = @TFV", this.Connection);
                            command.Parameters.Add(new NpgsqlParameter("TFV",    NpgsqlDbType.Integer));
                            command.Parameters.Add(new NpgsqlParameter("PREDID", NpgsqlDbType.Bigint));
                            command.Parameters.Add(new NpgsqlParameter("OBJID",  NpgsqlDbType.Bigint));
                            command.Parameters["TFV"].Value    = RDFModelEnums.RDFTripleFlavors.SPO;
                            command.Parameters["PREDID"].Value = pred.PatternMemberID;
                            command.Parameters["OBJID"].Value  = obj.PatternMemberID;
                        }
                        else {
                            if (lit != null) {
                                //->->P->L
                                command = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE predicateid = @PREDID AND objectid = @OBJID AND tripleflavor = @TFV", this.Connection);
                                command.Parameters.Add(new NpgsqlParameter("TFV",    NpgsqlDbType.Integer));
                                command.Parameters.Add(new NpgsqlParameter("PREDID", NpgsqlDbType.Bigint));
                                command.Parameters.Add(new NpgsqlParameter("OBJID",  NpgsqlDbType.Bigint));
                                command.Parameters["TFV"].Value    = RDFModelEnums.RDFTripleFlavors.SPL;
                                command.Parameters["PREDID"].Value = pred.PatternMemberID;
                                command.Parameters["OBJID"].Value  = lit.PatternMemberID;
                            }
                            else {
                                //->->P->
                                command = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE predicateid = @PREDID", this.Connection);
                                command.Parameters.Add(new NpgsqlParameter("PREDID", NpgsqlDbType.Bigint));
                                command.Parameters["PREDID"].Value = pred.PatternMemberID;
                            }
                        }
                    }
                    else {
                        if (obj     != null) {
                            //->->->O
                            command  = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE objectid = @OBJID AND tripleflavor = @TFV", this.Connection);
                            command.Parameters.Add(new NpgsqlParameter("TFV",   NpgsqlDbType.Integer));
                            command.Parameters.Add(new NpgsqlParameter("OBJID", NpgsqlDbType.Bigint));
                            command.Parameters["TFV"].Value   = RDFModelEnums.RDFTripleFlavors.SPO;
                            command.Parameters["OBJID"].Value = obj.PatternMemberID;
                        }
                        else {
                            if (lit != null) {
                                //->->->L
                                command = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\" WHERE objectid = @OBJID AND tripleflavor = @TFV", this.Connection);
                                command.Parameters.Add(new NpgsqlParameter("TFV",   NpgsqlDbType.Integer));
                                command.Parameters.Add(new NpgsqlParameter("OBJID", NpgsqlDbType.Bigint));
                                command.Parameters["TFV"].Value   = RDFModelEnums.RDFTripleFlavors.SPL;
                                command.Parameters["OBJID"].Value = lit.PatternMemberID;
                            }
                            else {
                                //->->->
                                command = new NpgsqlCommand("SELECT tripleflavor, context, subject, predicate, object FROM public.\"quadruples\"", this.Connection);
                            }
                        }
                    }
                }
            }

            //Prepare and execute command
            try {

                //Open connection
                this.Connection.Open();

                //Prepare command
                command.Prepare();

                //Execute command
                using  (var quadruples = command.ExecuteReader()) {
                    if (quadruples.HasRows) {
                        while (quadruples.Read()) {
                            result.AddQuadruple(RDFStoreUtilities.ParseQuadruple(quadruples));
                        }
                    }
                }

                //Close connection
                this.Connection.Close();

            }
            catch (Exception ex) {

                //Close connection
                this.Connection.Close();

                //Propagate exception
                throw new RDFStoreException("Cannot read data from PostgreSQL store because: " + ex.Message, ex);

            }

            return result;
        }
        #endregion

		#region Diagnostics
        /// <summary>
        /// Performs the preliminary diagnostics controls on the underlying PostgreSQL database
        /// </summary>
        private RDFStoreEnums.RDFStoreSQLErrors Diagnostics() {
            try {

                //Open connection
                this.Connection.Open();

                //Create command
                var command     = new NpgsqlCommand("SELECT COUNT(*) FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND c.relname = 'quadruples';", this.Connection);

                //Execute command
                var result      = Int32.Parse(command.ExecuteScalar().ToString());

                //Close connection
                this.Connection.Close();

                //Return the diagnostics state
                return (result == 0 ? RDFStoreEnums.RDFStoreSQLErrors.QuadruplesTableNotFound : RDFStoreEnums.RDFStoreSQLErrors.NoErrors);

            }
            catch {

                //Close connection
                this.Connection.Close();

                //Return the diagnostics state
                return RDFStoreEnums.RDFStoreSQLErrors.InvalidDataSource;

            }
        }

        /// <summary>
        /// Prepares the underlying PostgreSQL database
        /// </summary>
        private void PrepareStore() {
            var check           = this.Diagnostics();

            //Prepare the database only if diagnostics has detected the missing of "Quadruples" table in the store
            if (check          == RDFStoreEnums.RDFStoreSQLErrors.QuadruplesTableNotFound) {
                try {

                    //Open connection
                    this.Connection.Open();

                    //Create & Execute command
                    var command = new NpgsqlCommand("CREATE TABLE public.\"quadruples\" (\"quadrupleid\" BIGINT NOT NULL PRIMARY KEY, \"tripleflavor\" INTEGER NOT NULL, \"contextid\" bigint NOT NULL, \"context\" VARCHAR NOT NULL, \"subjectid\" BIGINT NOT NULL, \"subject\" VARCHAR NOT NULL, \"predicateid\" BIGINT NOT NULL, \"predicate\" VARCHAR NOT NULL, \"objectid\" BIGINT NOT NULL, \"object\" VARCHAR NOT NULL);CREATE INDEX \"idx_contextid\" ON public.\"quadruples\" USING btree (\"contextid\");CREATE INDEX \"idx_subjectid\" ON public.\"quadruples\" USING btree (\"subjectid\");CREATE INDEX \"idx_predicateid\" ON public.\"quadruples\" USING btree (\"predicateid\");CREATE INDEX \"idx_objectid\" ON public.\"quadruples\" USING btree (\"objectid\",\"tripleflavor\");CREATE INDEX \"idx_subjectid_predicateid\" ON public.\"quadruples\" USING btree (\"subjectid\",\"predicateid\");CREATE INDEX \"idx_subjectid_objectid\" ON public.\"quadruples\" USING btree (\"subjectid\",\"objectid\",\"tripleflavor\");CREATE INDEX \"idx_predicateid_objectid\" ON public.\"quadruples\" USING btree (\"predicateid\",\"objectid\",\"tripleflavor\");", this.Connection);
                    command.ExecuteNonQuery();

                    //Close connection
                    this.Connection.Close();

                }
                catch (Exception ex) {

                    //Close connection
                    this.Connection.Close();

                    //Propagate exception
                    throw new RDFStoreException("Cannot prepare PostgreSQL store because: " + ex.Message, ex);

                }
            }

            //Otherwise, an exception must be thrown because it has not been possible to connect to the instance/database
            else if (check     == RDFStoreEnums.RDFStoreSQLErrors.InvalidDataSource) {
                throw new RDFStoreException("Cannot prepare PostgreSQL store because: unable to connect to the server instance or to open the selected database.");
            }
        }
        #endregion		
		
        #region Optimize
        /// <summary>
        /// Executes a special command to optimize PostgreSQL store
        /// </summary>
        public void OptimizeStore() {
            try {

                //Open connection
                this.Connection.Open();

                //Create command
                var command = new NpgsqlCommand("VACUUM ANALYZE public.\"quadruples\"", this.Connection);

                //Execute command
                command.ExecuteNonQuery();

                //Close connection
                this.Connection.Close();

            }
            catch (Exception ex) {

                //Close connection
                this.Connection.Close();

                //Propagate exception
                throw new RDFStoreException("Cannot optimize PostgreSQL store because: " + ex.Message, ex);

            }
        }
        #endregion

        #endregion

    }

}