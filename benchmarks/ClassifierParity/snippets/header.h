#ifndef DEMO_H
#define DEMO_H
typedef struct node { struct node *next; int value; } node_t;
int list_length(const node_t *head);
#endif
