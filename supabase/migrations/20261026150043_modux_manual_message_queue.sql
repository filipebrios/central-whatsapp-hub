alter table public.agenda_message_queue drop constraint agenda_message_queue_state_check;
alter table public.agenda_message_queue add constraint agenda_message_queue_state_check
check(state in ('preparada','tratada_manual','enviada_manual','erro','cancelada'));
revoke all on public.agenda_message_queue from anon,authenticated;
grant select,insert on public.agenda_message_queue to authenticated;
grant update(state) on public.agenda_message_queue to authenticated;

create or replace function app_private.agenda_queue_stamp()
returns trigger language plpgsql security invoker set search_path=''
as $$
begin
  if tg_op='INSERT' then
    new.created_by=(select auth.uid()); new.created_at=now();
  else
    if new.id is distinct from old.id or new.company_id is distinct from old.company_id
      or new.appointment_id is distinct from old.appointment_id or new.template_key is distinct from old.template_key
      or new.occurrence_key is distinct from old.occurrence_key or new.created_by is distinct from old.created_by
      or new.created_at is distinct from old.created_at then
      raise exception 'Identidade da mensagem não pode ser alterada';
    end if;
    if new.state is distinct from old.state then
      new.handled_by=(select auth.uid()); new.handled_at=now();
    end if;
  end if;
  new.updated_at=now();
  return new;
end;
$$;
create trigger agenda_queue_stamp before insert or update on public.agenda_message_queue
for each row execute function app_private.agenda_queue_stamp();

create or replace function public.agenda_prepare_manual_message(
 p_company_id uuid,p_appointment_id uuid,p_template_key text,p_expected_revision integer
) returns public.agenda_message_queue
language plpgsql security invoker set search_path=''
as $$
declare
  a public.agenda_appointments;
  job public.agenda_message_queue;
  occurrence text;
begin
  if (select auth.uid()) is null then raise exception using errcode='42501',message='Autenticação necessária'; end if;
  select * into a from public.agenda_appointments
  where company_id=p_company_id and id=p_appointment_id and deleted_at is null for share;
  if not found then raise exception using errcode='42501',message='Atendimento não autorizado'; end if;
  if a.revision is distinct from p_expected_revision then
    raise exception using errcode='40001',message='Atendimento mudou; atualize antes de preparar';
  end if;
  if p_template_key not in ('confirmacao','lembrete_d1','lembrete_d0','pos_venda','avaliacao_google','reagendamento') then
    raise exception using errcode='22023',message='Modelo inválido';
  end if;
  if a.status in ('cancelado','cancelar') then
    raise exception using errcode='22023',message='Atendimento cancelado';
  end if;
  if p_template_key in ('pos_venda','avaliacao_google') then
    if a.status<>'concluido' or a.service_completed_date is null
      or a.service_completed_date>(now() at time zone 'America/Sao_Paulo')::date-7 then
      raise exception using errcode='22023',message='Pós-venda disponível após sete dias da conclusão';
    end if;
    if p_template_key='avaliacao_google' and coalesce(a.feedback_score,-1)<8 then
      raise exception using errcode='22023',message='Avaliação ainda não registrada para esta rotina';
    end if;
    occurrence=a.service_completed_date::text;
  else
    if a.date is null or a.time is null or a.status='concluido' then
      raise exception using errcode='22023',message='Confira a data e a situação do atendimento';
    end if;
    occurrence=a.date::text||'T'||to_char(a.time,'HH24:MI');
  end if;
  insert into public.agenda_message_queue(company_id,appointment_id,template_key,occurrence_key,state)
  values(p_company_id,p_appointment_id,p_template_key,occurrence,'preparada')
  on conflict(company_id,appointment_id,template_key,occurrence_key) do nothing;
  select * into job from public.agenda_message_queue
  where company_id=p_company_id and appointment_id=p_appointment_id and template_key=p_template_key and occurrence_key=occurrence;
  return job;
end;
$$;
revoke all on function public.agenda_prepare_manual_message(uuid,uuid,text,integer) from public,anon;
grant execute on function public.agenda_prepare_manual_message(uuid,uuid,text,integer) to authenticated;

insert into public.agenda_message_templates(company_id,key,body)
select c.id,t.key,t.body from public.agenda_companies c cross join(values
 ('confirmacao','Olá, {cliente}! Aqui é da São Bento. Podemos confirmar o serviço de {servico} para {data}, às {hora}, no endereço {endereco}? Por favor, responda para confirmar ou solicitar reagendamento.'),
 ('lembrete_d1','Olá, {cliente}! Lembramos que seu atendimento de {servico} com a São Bento está agendado para amanhã, {data}, às {hora}. Endereço: {endereco}. Se precisar reagendar, avise nossa equipe.'),
 ('lembrete_d0','Olá, {cliente}! Seu atendimento de {servico} com a São Bento está agendado para hoje, {data}, às {hora}. Endereço: {endereco}. Estamos à disposição.'),
 ('pos_venda','Olá, {cliente}! Aqui é da São Bento. Gostaríamos de saber como foi o atendimento de {servico}. De 0 a 10, que nota você dá ao nosso serviço? Se desejar, conte também como podemos melhorar.'),
 ('reagendamento','Olá, {cliente}! Aqui é da São Bento. Precisamos combinar uma nova data para seu atendimento de {servico}. Qual dia e horário ficam melhores para você?')
) as t(key,body) on conflict(company_id,key) do nothing;
