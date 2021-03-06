﻿using Proteomics;
using System;
using System.Linq;
using System.Collections.Generic;

namespace ProteoformSuiteInternal
{
    public class ProteinWithGoTerms : Protein
    {
        public List<string> AccessionList { get; set; }
        public List<GoTerm> GoTerms { get; set; }

        public ProteinWithGoTerms(string sequence, string accession, List<Tuple<string, string>> gene_names, IDictionary<int, List<Modification>> oneBasedModifications, int?[] oneBasedBeginPositionsForProteolysisProducts, int?[] oneBasedEndPositionsForProteolysisProducts, string[] oneBasedProteolysisProductsTypes, string name, string full_name, bool isDecoy, bool isContaminant, IEnumerable<DatabaseReference> databaseReferences, IEnumerable<GoTerm> goTerms)
            : base(sequence, accession, gene_names, oneBasedModifications, oneBasedBeginPositionsForProteolysisProducts, oneBasedEndPositionsForProteolysisProducts, oneBasedProteolysisProductsTypes, name, full_name, isDecoy, isContaminant, databaseReferences.ToList())
        {
            this.GoTerms = goTerms.ToList();
            this.AccessionList = new List<string> { accession };
        }
    }
}
