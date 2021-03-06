﻿using Proteomics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UsefulProteomicsDatabases;

namespace ProteoformSuiteInternal
{
    public class TheoreticalProteoformDatabase
    {

        #region Public Fields

        //Protein
        public Dictionary<InputFile, Protein[]> theoretical_proteins = new Dictionary<InputFile, Protein[]>();
        public ProteinWithGoTerms[] expanded_proteins = new ProteinWithGoTerms[0];

        //Modifications
        public Dictionary<string, List<Modification>> uniprotModifications = new Dictionary<string, List<Modification>>();
        public List<ModificationWithMass> variableModifications = new List<ModificationWithMass>();
        public List<ModificationWithMass> all_mods_with_mass = new List<ModificationWithMass>();
        public Dictionary<ModificationWithMass, UnlocalizedModification> unlocalized_lookup = new Dictionary<ModificationWithMass, UnlocalizedModification>();

        //PtmSets
        public List<PtmSet> all_possible_ptmsets = new List<PtmSet>();
        public Dictionary<double, List<PtmSet>> possible_ptmset_dictionary = new Dictionary<double, List<PtmSet>>();
        public List<PtmSet> acceptable_ptm_sets = new List<PtmSet>();

        //Settings
        public bool limit_triples_and_greater = true;

        #endregion Public Fields

        #region Private Fields

        private Dictionary<char, double> aaIsotopeMassList;

        #endregion Private Fields

        #region Public Methods

        public void get_theoretical_proteoforms(string current_directory)
        {
            if (!ready_to_make_database(current_directory))
                return;

            //Clear out data from potential previous runs
            foreach(ProteoformCommunity community in SaveState.lollipop.decoy_proteoform_communities.Values)
            {
                community.theoretical_proteoforms = new TheoreticalProteoform[0];
            }
            theoretical_proteins.Clear();

            //Read the UniProt-XML and ptmlist
            List<ModificationWithLocation> all_known_modifications = SaveState.lollipop.get_files(SaveState.lollipop.input_files, Purpose.PtmList).SelectMany(file => PtmListLoader.ReadModsFromFile(file.complete_path)).ToList();
            uniprotModifications = make_modification_dictionary(all_known_modifications);

            Dictionary<string, Modification> um;
            Parallel.ForEach(SaveState.lollipop.get_files(SaveState.lollipop.input_files, Purpose.ProteinDatabase).ToList(), database =>
            {
                lock (theoretical_proteins) theoretical_proteins.Add(database, ProteinDbLoader.LoadProteinXML(database.complete_path, false, all_known_modifications, database.ContaminantDB, SaveState.lollipop.mod_types_to_exclude, out um).ToArray());
                lock (all_known_modifications) all_known_modifications.AddRange(ProteinDbLoader.GetPtmListFromProteinXml(database.complete_path).OfType<ModificationWithLocation>().Where(m => !SaveState.lollipop.mod_types_to_exclude.Contains(m.modificationType)));
            });

            foreach (string filename in Directory.GetFiles(Path.Combine(current_directory, "Mods")))
            {
                var new_mods = !filename.EndsWith("variable.txt") || SaveState.lollipop.methionine_oxidation ?
                    PtmListLoader.ReadModsFromFile(filename) :
                    new List<ModificationWithLocation>(); // Empty variable modifications if not selected
                if (filename.EndsWith("variable.txt"))
                    variableModifications = new_mods.OfType<ModificationWithMass>().ToList();
                if (filename.EndsWith("intact_mods.txt"))
                {
                    List<double> old_mods = all_known_modifications.OfType<ModificationWithMass>().Select(m => m.monoisotopicMass).ToList();
                    new_mods = new_mods.OfType<ModificationWithMass>().Where(m => !old_mods.Contains(m.monoisotopicMass)); // get rid of the unlocalized mods if they're already present
                }
                all_known_modifications.AddRange(new_mods);
            }

            all_known_modifications = new HashSet<ModificationWithLocation>(all_known_modifications).ToList();
            uniprotModifications = make_modification_dictionary(all_known_modifications);
            all_mods_with_mass = uniprotModifications.SelectMany(kv => kv.Value).OfType<ModificationWithMass>().Concat(variableModifications).ToList();
            SaveState.lollipop.modification_ranks = rank_mods(theoretical_proteins, variableModifications, all_mods_with_mass);

            unlocalized_lookup = make_unlocalized_lookup(all_mods_with_mass.Concat(new List<ModificationWithMass> { new Ptm().modification }));
            load_unlocalized_names(Path.Combine(Environment.CurrentDirectory, "Mods", "stored_mods.modnames"));

            //Generate all two-member sets and all three-member (or greater) sets of the same modification (three-member combinitorics gets out of hand for assignment)
            all_possible_ptmsets = PtmCombos.generate_all_ptmsets(Math.Min(2, SaveState.lollipop.max_ptms), all_mods_with_mass, SaveState.lollipop.modification_ranks, SaveState.lollipop.mod_rank_first_quartile / 2).ToList();
            for (int i = 2; i < SaveState.lollipop.max_ptms + 1; i++)
            {
                all_possible_ptmsets.AddRange(all_mods_with_mass.Select(m => new PtmSet(Enumerable.Repeat(new Ptm(-1, m), i).ToList(), SaveState.lollipop.modification_ranks, SaveState.lollipop.mod_rank_first_quartile / 2)));
            }

            //Generate lookup table for ptm sets based on rounded mass of eligible PTMs -- used in forming ET relations
            possible_ptmset_dictionary = make_ptmset_dictionary();

            expanded_proteins = expand_protein_entries(theoretical_proteins.Values.SelectMany(p => p).ToArray());
            aaIsotopeMassList = new AminoAcidMasses(SaveState.lollipop.carbamidomethylation, SaveState.lollipop.natural_lysine_isotope_abundance, SaveState.lollipop.neucode_light_lysine, SaveState.lollipop.neucode_heavy_lysine).AA_Masses;
            if (SaveState.lollipop.combine_identical_sequences) expanded_proteins = group_proteins_by_sequence(expanded_proteins);

            expanded_proteins = expanded_proteins.OrderBy(x => x.OneBasedPossibleLocalizedModifications.Count).ToArray(); // Take on harder problems first to use parallelization more effectively
            process_entries(expanded_proteins, variableModifications);
            process_decoys(expanded_proteins, variableModifications);

            if (SaveState.lollipop.combine_theoretical_proteoforms_byMass)
            {
                SaveState.lollipop.target_proteoform_community.theoretical_proteoforms = group_proteoforms_by_mass(SaveState.lollipop.target_proteoform_community.theoretical_proteoforms);
                foreach (ProteoformCommunity community in SaveState.lollipop.decoy_proteoform_communities.Values)
                {
                   community.theoretical_proteoforms = group_proteoforms_by_mass(community.theoretical_proteoforms);
                }
            }
        }

        //Generate lookup table for ptm sets based on rounded mass of eligible PTMs -- used in forming ET relations
        public Dictionary<double, List<PtmSet>> make_ptmset_dictionary()
        {
            Dictionary<double, List<PtmSet>> possible_ptmsets = new Dictionary<double, List<PtmSet>>();
            foreach (PtmSet set in all_possible_ptmsets.Where(s => s.ptm_combination.Count == 1 || !s.ptm_combination.Select(ptm => ptm.modification).Any(m => m.monoisotopicMass == 0)).ToList())
            {
                for (int i = 0; i < 10; i++)
                {
                    double midpoint = Math.Round(set.mass, 1) - 0.5 + i * 0.1;
                    if (possible_ptmsets.TryGetValue(midpoint, out List<PtmSet> a)) a.Add(set);
                    else possible_ptmsets.Add(midpoint, new List<PtmSet> { set });
                }
            }
            return possible_ptmsets;
        }

        public Dictionary<string, List<Modification>> make_modification_dictionary(IEnumerable<ModificationWithLocation> all_modifications)
        {
            Dictionary<string, List<Modification>> mod_dict = new Dictionary<string, List<Modification>>();
            foreach (var nice in all_modifications)
            {
                if (mod_dict.TryGetValue(nice.id, out List<Modification> val)) val.Add(nice);
                else mod_dict.Add(nice.id, new List<Modification> { nice });
            }
            return mod_dict;
        }

        public Dictionary<double, int> rank_mods(Dictionary<InputFile, Protein[]> theoretical_proteins, IEnumerable<ModificationWithMass> variable_modifications, IEnumerable<ModificationWithMass> all_mods_with_mass)
        {
            Dictionary<double, int> mod_counts = new Dictionary<double, int>();

            foreach (ModificationWithMass m in theoretical_proteins.SelectMany(kv => kv.Value).SelectMany(p => p.OneBasedPossibleLocalizedModifications).SelectMany(kv => kv.Value).OfType<ModificationWithMass>().ToList())
            {
                if (!mod_counts.TryGetValue(m.monoisotopicMass, out int b)) mod_counts.Add(m.monoisotopicMass, 1);
                else mod_counts[m.monoisotopicMass]++;
            }
            List<KeyValuePair<double, int>> ordered_mod_counts = mod_counts.OrderByDescending(kv => kv.Value).ToList();

            int rank = 3;
            int last_count = 0;
            Dictionary<double, int> mod_ranks = new Dictionary<double, int>();
            foreach (KeyValuePair<double, int> mod_count in ordered_mod_counts)
            {
                mod_ranks.Add(mod_count.Key, rank);
                if (mod_count.Value < last_count) rank++;
                last_count = mod_count.Value;
            }

            //Give unmodified the best rank
            if (!mod_ranks.TryGetValue(0, out int a)) mod_ranks.Add(0, 0);
            else mod_ranks[0] = 0;

            //Give variable mods a good score
            foreach (ModificationWithMass m in variable_modifications)
            {
                if (!mod_ranks.TryGetValue(m.monoisotopicMass, out int b)) mod_ranks.Add(m.monoisotopicMass, 2);
                else mod_ranks[m.monoisotopicMass] = 2;
            }

            List<int> ranks = mod_ranks.Values.OrderBy(x => x).ToList();
            SaveState.lollipop.mod_rank_first_quartile = ranks[ranks.Count / 4];
            SaveState.lollipop.mod_rank_second_quartile = ranks[2 * ranks.Count / 4];
            SaveState.lollipop.mod_rank_third_quartile = ranks[3 * ranks.Count / 4];
            SaveState.lollipop.mod_rank_sum_threshold = ranks.Max();

            //Give the remaining mods the threshold value
            foreach (ModificationWithMass m in all_mods_with_mass)
            {
                if (!mod_ranks.TryGetValue(m.monoisotopicMass, out int lkj))
                    mod_ranks.Add(m.monoisotopicMass, SaveState.lollipop.mod_rank_sum_threshold);
            }

            return mod_ranks;
        }

        public static ProteinWithGoTerms[] expand_protein_entries(Protein[] proteins)
        {
            List<ProteinWithGoTerms> expanded_prots = new List<ProteinWithGoTerms>();
            foreach (Protein p in proteins)
            {
                List<ProteinWithGoTerms> new_prots = new List<ProteinWithGoTerms>();

                //Add full length product
                int begin = 1;
                int end = p.BaseSequence.Length;
                List<GoTerm> goTerms = p.DatabaseReferences.Where(reference => reference.Type == "GO").Select(reference => new GoTerm(reference)).ToList();
                int startPosAfterCleavage = Convert.ToInt32(SaveState.lollipop.methionine_cleavage && p.BaseSequence.StartsWith("M"));
                new_prots.Add(new ProteinWithGoTerms(
                    p.BaseSequence.Substring(begin + startPosAfterCleavage - 1, end - (begin + startPosAfterCleavage) + 1),
                    p.Accession + "_" + (begin + startPosAfterCleavage).ToString() + "full" + end.ToString(),
                    p.GeneNames.ToList(),
                    p.OneBasedPossibleLocalizedModifications,
                    new int?[] { begin + startPosAfterCleavage },
                    new int?[] { end },
                    new string[] { SaveState.lollipop.methionine_cleavage && p.BaseSequence.StartsWith("M") ? "full-met-cleaved" : "full" },
                    p.Name, p.FullName, p.IsDecoy, p.IsContaminant, p.DatabaseReferences, goTerms));

                //Add fragments
                List<ProteolysisProduct> products = p.ProteolysisProducts.ToList();
                foreach (ProteolysisProduct product in p.ProteolysisProducts)
                {
                    string feature_type = product.Type.Replace(' ', '-');
                    if (!(feature_type == "peptide" || feature_type == "propeptide" || feature_type == "chain" || feature_type == "signal-peptide")
                            || !product.OneBasedBeginPosition.HasValue
                            || !product.OneBasedEndPosition.HasValue)
                        continue;
                    int feature_begin = (int)product.OneBasedBeginPosition;
                    int feature_end = (int)product.OneBasedEndPosition;
                    if (feature_begin < 1 || feature_end < 1)
                        continue;
                    bool feature_is_just_met_cleavage = SaveState.lollipop.methionine_cleavage && feature_begin == begin + 1 && feature_end == end;
                    string subsequence = p.BaseSequence.Substring(feature_begin - 1, feature_end - feature_begin + 1);
                    Dictionary<int, List<Modification>> segmented_ptms = p.OneBasedPossibleLocalizedModifications.Where(kv => kv.Key >= feature_begin && kv.Key <= feature_end).ToDictionary(kv => kv.Key, kv => kv.Value);
                    if (!feature_is_just_met_cleavage && subsequence.Length != p.BaseSequence.Length && subsequence.Length >= SaveState.lollipop.min_peptide_length)
                        new_prots.Add(new ProteinWithGoTerms(
                            subsequence,
                            p.Accession + "_" + feature_begin.ToString() + "frag" + feature_end.ToString(),
                            p.GeneNames.ToList(),
                            segmented_ptms,
                            new int?[] { feature_begin },
                            new int?[] { feature_end },
                            new string[] { feature_type },
                            p.Name, p.FullName, p.IsDecoy, p.IsContaminant, p.DatabaseReferences, goTerms));
                }
                expanded_prots.AddRange(new_prots);
            }
            return expanded_prots.ToArray();
        }

        public ProteinSequenceGroup[] group_proteins_by_sequence(IEnumerable<ProteinWithGoTerms> proteins)
        {
            Dictionary<string, List<ProteinWithGoTerms>> sequence_groupings = new Dictionary<string, List<ProteinWithGoTerms>>();
            foreach (ProteinWithGoTerms p in proteins)
            {
                if (sequence_groupings.ContainsKey(p.BaseSequence)) sequence_groupings[p.BaseSequence].Add(p);
                else sequence_groupings.Add(p.BaseSequence, new List<ProteinWithGoTerms> { p });
            }
            return sequence_groupings.Select(kv => new ProteinSequenceGroup(kv.Value.OrderByDescending(p => p.IsContaminant ? 1 : 0))).ToArray();
        }

        public TheoreticalProteoformGroup[] group_proteoforms_by_mass(IEnumerable<TheoreticalProteoform> theoreticals)
        {
            Dictionary<double, List<TheoreticalProteoform>> mass_groupings = new Dictionary<double, List<TheoreticalProteoform>>();
            foreach (TheoreticalProteoform t in theoreticals)
            {
                if (mass_groupings.ContainsKey(t.modified_mass)) mass_groupings[t.modified_mass].Add(t);
                else mass_groupings.Add(t.modified_mass, new List<TheoreticalProteoform> { t });
            }
            return mass_groupings.Select(kv => new TheoreticalProteoformGroup(kv.Value.OrderByDescending(t => t.contaminant ? 1 : 0))).ToArray();
        }

        /// <summary>
        /// Requires at least one ProteinDatabase input file and one input file listing modifications.
        /// </summary>
        /// <returns></returns>
        public bool ready_to_make_database(string current_directory)
        {
            Loaders.LoadElements(Path.Combine(current_directory, "elements.dat"));
            List<InputFile> proteinDbs = SaveState.lollipop.get_files(SaveState.lollipop.input_files, Purpose.ProteinDatabase).ToList();
            return proteinDbs.Count > 0
                && (proteinDbs.Any(file => ProteinDbLoader.GetPtmListFromProteinXml(file.complete_path).Count > 0) 
                || SaveState.lollipop.get_files(SaveState.lollipop.input_files, Purpose.PtmList).Count() > 0);
        }

        #endregion Public Methods

        #region Unlocalized Mods Public Methods

        public Dictionary<ModificationWithMass, UnlocalizedModification> make_unlocalized_lookup(IEnumerable<ModificationWithMass> all_modifications)
        {
            Dictionary<ModificationWithMass, UnlocalizedModification> mod_dict = new Dictionary<ModificationWithMass, UnlocalizedModification>();
            foreach (var nice in all_modifications)
            {
                if (!mod_dict.TryGetValue(nice, out UnlocalizedModification val))
                    mod_dict.Add(nice, new UnlocalizedModification(nice));
            }
            return mod_dict;
        }

        public void save_unlocalized_names(string filepath)
        {
            if (!Directory.Exists(Path.GetDirectoryName(filepath)))
                return;

            using (StreamWriter writer = new StreamWriter(filepath))
            {
                foreach (var unloc in unlocalized_lookup)
                {
                    writer.WriteLine(unloc.Key.id + "\t" + unloc.Value.id + "\t" + unloc.Value.ptm_count.ToString() + "\t" + unloc.Value.require_proteoform_without_mod.ToString());
                }
            }
        }

        public void load_unlocalized_names(string filepath)
        {
            if (!File.Exists(filepath))
                return;

            Dictionary<string, string[]> mod_info = new Dictionary<string, string[]>();
            using (StreamReader reader = new StreamReader(filepath))
            {
                while (true)
                {
                    string a = reader.ReadLine();
                    if (a == null)
                        break;
                    string[] line = a.Split('\t');
                    if (!mod_info.TryGetValue(line[0], out string[] info))
                        mod_info.Add(line[0], line);
                }
            }

            foreach (var mod_unlocalized in unlocalized_lookup)
            {
                if (mod_info.TryGetValue(mod_unlocalized.Key.id, out string[] new_info))
                {
                    mod_unlocalized.Value.id = new_info[1];
                    mod_unlocalized.Value.ptm_count = Convert.ToInt32(new_info[2]);
                    mod_unlocalized.Value.require_proteoform_without_mod = Convert.ToBoolean(new_info[3]);
                }
            }
        }

        public void amend_unlocalized_names(string filepath)
        {
            if (!File.Exists(filepath))
                return;

            Dictionary<string, string[]> mod_info = new Dictionary<string, string[]>();
            using (StreamReader reader = new StreamReader(filepath))
            {
                while (true)
                {
                    string a = reader.ReadLine();
                    if (a == null)
                        break;
                    string[] line = a.Split('\t');
                    if (!mod_info.TryGetValue(line[0], out string[] info))
                        mod_info.Add(line[0], line);
                }
            }

            foreach (var mod_unlocalized in unlocalized_lookup)
            {
                string[] new_info = new string[] { mod_unlocalized.Key.id, mod_unlocalized.Value.id, mod_unlocalized.Value.ptm_count.ToString(), mod_unlocalized.Value.require_proteoform_without_mod.ToString() };
                if (mod_info.TryGetValue(mod_unlocalized.Key.id, out string[] x))
                    mod_info[mod_unlocalized.Key.id] = new_info;
                else
                    mod_info.Add(mod_unlocalized.Key.id, new_info);
            }

            using (StreamWriter writer = new StreamWriter(filepath))
            {
                foreach (var unloc in mod_info.Values.OrderBy(x => x[0]))
                {
                    writer.WriteLine(String.Join("\t", unloc));
                }
            }
        }

        #endregion Unlocalized Mods Public Methods

        #region Private Methods

        private void process_entries(IEnumerable<ProteinWithGoTerms> expanded_proteins, IEnumerable<ModificationWithMass> variableModifications)
        {
            List<TheoreticalProteoform> theoretical_proteoforms = new List<TheoreticalProteoform>();
            Parallel.ForEach(expanded_proteins, p => EnterTheoreticalProteformFamily(p.BaseSequence, p, p.Accession, theoretical_proteoforms, -100, variableModifications));
            SaveState.lollipop.target_proteoform_community.theoretical_proteoforms = theoretical_proteoforms.ToArray();
        }

        private void process_decoys(ProteinWithGoTerms[] expanded_proteins, IEnumerable<ModificationWithMass> variableModifications)
        {
            SaveState.lollipop.decoy_proteoform_communities.Clear();
            Parallel.For(0, SaveState.lollipop.decoy_databases, decoyNumber =>
            {
                List<TheoreticalProteoform> decoy_proteoforms = new List<TheoreticalProteoform>();
                string giantProtein = GetOneGiantProtein(expanded_proteins, SaveState.lollipop.methionine_cleavage); //Concatenate a giant protein out of all protein read from the UniProt-XML, and construct target and decoy proteoform databases
                ProteinWithGoTerms[] shuffled_proteins = new ProteinWithGoTerms[expanded_proteins.Length];
                Array.Copy(expanded_proteins, shuffled_proteins, expanded_proteins.Length);
                new Random().Shuffle(shuffled_proteins); //randomize order of protein array

                int prevLength = 0;
                Parallel.ForEach(shuffled_proteins, p =>
                {
                    string hunk = giantProtein.Substring(prevLength, p.BaseSequence.Length);
                    prevLength += p.BaseSequence.Length;
                    EnterTheoreticalProteformFamily(hunk, p, p.Accession + "_DECOY_" + decoyNumber, decoy_proteoforms, decoyNumber, variableModifications);
                });

                lock (SaveState.lollipop.decoy_proteoform_communities)
                {
                    SaveState.lollipop.decoy_proteoform_communities.Add(SaveState.lollipop.decoy_community_name_prefix + decoyNumber, new ProteoformCommunity());
                    SaveState.lollipop.decoy_proteoform_communities[SaveState.lollipop.decoy_community_name_prefix + decoyNumber].theoretical_proteoforms = decoy_proteoforms.ToArray();
                    SaveState.lollipop.decoy_proteoform_communities[SaveState.lollipop.decoy_community_name_prefix + decoyNumber].experimental_proteoforms =
                    SaveState.lollipop.target_proteoform_community.experimental_proteoforms.Select(e => new ExperimentalProteoform(e)).ToArray();
                }
            });
        }

        private void EnterTheoreticalProteformFamily(string seq, ProteinWithGoTerms prot, string accession, List<TheoreticalProteoform> theoretical_proteoforms, int decoy_number, IEnumerable<ModificationWithMass> variableModifications)
        {
            //Calculate the properties of this sequence
            double unmodified_mass = TheoreticalProteoform.CalculateProteoformMass(seq, aaIsotopeMassList);
            int lysine_count = seq.Split('K').Length - 1;
            bool check_contaminants = theoretical_proteins.Any(item => item.Key.ContaminantDB);

            //Figure out the possible ptm sets
            Dictionary<int, List<Modification>> possibleLocalizedMods = new Dictionary<int, List<Modification>>(prot.OneBasedPossibleLocalizedModifications);
            foreach (ModificationWithMass m in variableModifications)
            {
                for (int i = 1; i <= prot.BaseSequence.Length; i++)
                {
                    if (prot.BaseSequence[i - 1].ToString() == m.motif.Motif)
                    {
                        if (!possibleLocalizedMods.TryGetValue(i, out List<Modification> a)) possibleLocalizedMods.Add(i, new List<Modification> { m });
                        else a.Add(m);
                    }
                }
            }

            List<PtmSet> unique_ptm_groups = PtmCombos.get_combinations(possibleLocalizedMods, SaveState.lollipop.max_ptms, SaveState.lollipop.modification_ranks, SaveState.lollipop.mod_rank_first_quartile / 2, limit_triples_and_greater);

            //Enumerate the ptm combinations with _P# to distinguish from the counts in ProteinSequenceGroups (_#G) and TheoreticalPfGps (_#T)
            int ptm_set_counter = 1;
            foreach (PtmSet ptm_set in unique_ptm_groups)
            {
                lock (theoretical_proteoforms) theoretical_proteoforms.Add(
                    new TheoreticalProteoform(
                        accession + "_P" + ptm_set_counter.ToString(),
                        prot.FullDescription + "_P" + ptm_set_counter.ToString() + (decoy_number < 0 ? "" : "_DECOY_" + decoy_number.ToString()),
                        new ProteinWithGoTerms[] { prot },
                        unmodified_mass,
                        lysine_count,
                        ptm_set,
                        decoy_number < 0,
                        check_contaminants,
                        theoretical_proteins)
                    );
                ptm_set_counter++;
            }
        }

        private string GetOneGiantProtein(IEnumerable<Protein> proteins, bool methionine_cleavage)
        {
            StringBuilder giantProtein = new StringBuilder(5000000); // this set-aside is autoincremented to larger values when necessary.
            foreach (Protein protein in proteins)
            {
                string sequence = protein.BaseSequence;
                bool isMetCleaved = methionine_cleavage && (sequence.Substring(0, 1) == "M");
                int startPosAfterMetCleavage = Convert.ToInt32(isMetCleaved);
                switch (protein.ProteolysisProducts.Select(p => p.Type).FirstOrDefault())
                {
                    case "chain":
                    case "signal peptide":
                    case "propeptide":
                    case "peptide":
                        giantProtein.Append(".");
                        break;
                    default:
                        giantProtein.Append("-");
                        break;
                }
                giantProtein.Append(sequence.Substring(startPosAfterMetCleavage));
            }
            return giantProtein.ToString();
        }

        #endregion Private Methods

    }
}
