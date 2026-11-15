# WashFlow midterm thesis progress report

The [compiled report](washflow-midterm-progress-report.pdf) is in English, with the Turkish abstract required by the template. The source adapts [Istanbul Arel University's thesis template](https://github.com/kocatekin/arel_thesis_latex_template/tree/866a986d808475984d49764073ad7f862fa2cd31). The included style and university logo are copied from that revision without modification. The body uses the template's Introduction, Background, Method, Results and Evaluation, and Conclusion chapters, with IEEEtran bibliography formatting.

## Build

From this directory, run either:

\`\`\`sh
tectonic --reruns 3 --outdir /tmp thesis.tex
\`\`\`

or

\`\`\`sh
latexmk -pdf thesis.tex
\`\`\`

The checked-in figure at \`figures/live-erd.pdf\` is the vector print conversion of [the repository's verified ER SVG](../database/live-erd.svg). It uses the SVG view box and carries the entire diagram. The architecture figure is drawn in \`thesis.tex\`. The build does not contact either local database.

Before university submission, replace the student name, student number, and supervisor placeholders in \`thesis.tex\`. The student must also complete the acknowledgement and review, complete, and sign the undertaking according to the university's instructions. No tracked screenshots of the assessed application flow were present at the report baseline. Add verified UI captures with synthetic data if required for the submission.
