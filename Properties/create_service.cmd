
sc.exe delete "Noraxon Service"
sc.exe create "Noraxon Service" binpath="%cd%/NoraxonService.exe"